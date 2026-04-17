// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.Linq;
using Dynamo.Graph.Annotations;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Notes;
using Dynamo.Graph.Workspaces;
using Dynamo.Models;
using Dynamo.ViewModels;
using Dynamo.Wpf.Extensions;

namespace BIBIM_MVP
{
    /// <summary>
    /// Section 3: Advanced Features - Node Manipulation
    /// Handles auto-grouping, smart annotations, node placement, and all 12 action types
    /// </summary>
    public class NodeManipulator
    {
        private readonly ViewLoadedParams _viewLoadedParams;

        public NodeManipulator(ViewLoadedParams viewLoadedParams)
        {
            _viewLoadedParams = viewLoadedParams;
        }

        #region Action Execution Entry Point

        /// <summary>
        /// Execute an analysis action and return result report
        /// </summary>
        public ActionExecutionResult ExecuteAction(AnalysisAction action)
        {
            var result = new ActionExecutionResult
            {
                ActionId = action.ActionId,
                ActionType = action.Type,
                DisplayText = action.DisplayText
            };

            string details;
            try
            {
                switch (action.Type)
                {
                    case ActionType.ADD_NODE:
                        result.Success = AddNodeAndConnect(action.NodeTypeToAdd, action.TargetNodeId, action.TargetPortIndex, out details);
                        result.Details = details;
                        break;

                    case ActionType.DELETE_NODE:
                        result.Success = DeleteNode(action.TargetNodeId, out details);
                        result.Details = details;
                        break;

                    case ActionType.REPLACE_NODE:
                        result.Success = ReplaceNode(action.TargetNodeId, action.NodeTypeToAdd, out details);
                        result.Details = details;
                        break;

                    case ActionType.CONNECT:
                        result.Success = ConnectNodes(action.SourceNodeId, action.SourcePortIndex, action.TargetNodeId, action.TargetPortIndex);
                        result.Details = result.Success
                            ? LocalizationService.Get("NodeManipulator_ConnectSuccess")
                            : LocalizationService.Get("NodeManipulator_ConnectFailed");
                        break;

                    case ActionType.DISCONNECT:
                        result.Success = DisconnectNodes(action.SourceNodeId, action.SourcePortIndex, action.TargetNodeId, action.TargetPortIndex, out details);
                        result.Details = details;
                        break;

                    case ActionType.RECONNECT:
                        result.Success = ReconnectNodes(action.SourceNodeId, action.SourcePortIndex, action.OldValue, action.TargetNodeId, action.TargetPortIndex, out details);
                        result.Details = details;
                        break;

                    case ActionType.FIX_CODE:
                        result.Success = FixPythonCode(action.TargetNodeId, action.OldValue, action.NewValue, out details);
                        result.Details = details;
                        break;

                    case ActionType.REPLACE_CODE:
                        result.Success = ReplacePythonCode(action.TargetNodeId, action.NewValue, out details);
                        result.Details = details;
                        break;

                    case ActionType.SET_VALUE:
                        result.Success = SetNodeValue(action.TargetNodeId, action.NewValue, out details);
                        result.Details = details;
                        break;

                    case ActionType.SET_LACING:
                        result.Success = SetNodeLacing(action.TargetNodeId, action.LacingMode, out details);
                        result.Details = details;
                        break;

                    case ActionType.GROUP_NODES:
                        result.Success = CreateGroup(action.NodeIds, action.GroupTitle, action.GroupColor);
                        result.Details = result.Success
                            ? LocalizationService.Format("NodeManipulator_GroupCreatedSuccess", action.GroupTitle)
                            : LocalizationService.Get("NodeManipulator_GroupCreateFailed");
                        break;

                    case ActionType.ADD_NOTE:
                        result.Success = AddNote(action.NoteText, action.NoteX, action.NoteY);
                        result.Details = result.Success
                            ? LocalizationService.Get("NodeManipulator_NoteAddSuccess")
                            : LocalizationService.Get("NodeManipulator_NoteAddFailed");
                        break;

                    default:
                        result.Success = false;
                        result.Details = LocalizationService.Get("NodeManipulator_UnknownActionType");
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Details = LocalizationService.Format("NodeManipulator_ExecuteError", ex.Message);
                Log($"ExecuteAction Error: {ex.Message}");
            }

            return result;
        }

        #endregion

        #region Action Type 1: ADD_NODE

        /// <summary>
        /// Create a new node and connect it to target
        /// </summary>
        private bool AddNodeAndConnect(string nodeTypeName, string targetNodeId, int targetPortIndex, out string details)
        {
            details = "";
            try
            {
                var dynamoViewModel = _viewLoadedParams.DynamoWindow?.DataContext as DynamoViewModel;
                var workspace = dynamoViewModel?.Model?.CurrentWorkspace;
                if (workspace == null)
                {
                    details = LocalizationService.Get("NodeManipulator_WorkspaceNotFound");
                    return false;
                }

                // Find target node
                var targetNode = workspace.Nodes.FirstOrDefault(n => n.GUID.ToString() == targetNodeId);
                if (targetNode == null)
                {
                    details = LocalizationService.Format("NodeManipulator_TargetNodeNotFound", targetNodeId);
                    return false;
                }

                // Store existing node GUIDs before creation
                var existingNodeIds = new HashSet<string>(workspace.Nodes.Select(n => n.GUID.ToString()));

                // Calculate position (left of target node)
                double newX = targetNode.X - 250;
                double newY = targetNode.Y;

                // Create the node
                if (!CreateNodeAtPosition(nodeTypeName, newX, newY))
                {
                    details = LocalizationService.Format("NodeManipulator_NodeCreateFailed", nodeTypeName);
                    return false;
                }

                // Find the newly created node (the one not in our previous set)
                var newNode = workspace.Nodes.FirstOrDefault(n => !existingNodeIds.Contains(n.GUID.ToString()));
                if (newNode == null)
                {
                    // Fallback: try last node
                    newNode = workspace.Nodes.LastOrDefault();
                }

                if (newNode == null)
                {
                    details = LocalizationService.Get("NodeManipulator_CreatedNodeNotFound");
                    return false;
                }

                // Connect new node's output to target's input
                if (newNode.OutPorts.Any() && targetNode.InPorts.Count > targetPortIndex)
                {
                    ConnectNodes(newNode.GUID.ToString(), 0, targetNodeId, targetPortIndex);
                }

                details = LocalizationService.Format("NodeManipulator_AddNodeConnectSuccess", nodeTypeName, targetNode.Name);
                return true;
            }
            catch (Exception ex)
            {
                details = LocalizationService.Format("NodeManipulator_GenericError", ex.Message);
                return false;
            }
        }

        #endregion

        #region Action Type 2: DELETE_NODE

        /// <summary>
        /// Delete a node from the workspace
        /// </summary>
        private bool DeleteNode(string nodeId, out string details)
        {
            details = "";
            try
            {
                var dynamoViewModel = _viewLoadedParams.DynamoWindow?.DataContext as DynamoViewModel;
                var workspace = dynamoViewModel?.Model?.CurrentWorkspace;
                if (workspace == null)
                {
                    details = LocalizationService.Get("NodeManipulator_WorkspaceNotFound");
                    return false;
                }

                var node = workspace.Nodes.FirstOrDefault(n => n.GUID.ToString() == nodeId);
                if (node == null)
                {
                    details = LocalizationService.Format("NodeManipulator_NodeNotFoundId", nodeId);
                    return false;
                }

                string nodeName = node.Name;

                // Use DeleteModelCommand
                var guids = new List<Guid> { node.GUID };
                dynamoViewModel.ExecuteCommand(new DynamoModel.DeleteModelCommand(guids));

                details = LocalizationService.Format("NodeManipulator_NodeDeleteSuccess", nodeName);
                return true;
            }
            catch (Exception ex)
            {
                details = LocalizationService.Format("NodeManipulator_DeleteError", ex.Message);
                return false;
            }
        }

        #endregion

        #region Action Type 3: REPLACE_NODE

        /// <summary>
        /// Replace a node with a different type, preserving connections where possible
        /// </summary>
        private bool ReplaceNode(string oldNodeId, string newNodeTypeName, out string details)
        {
            details = "";
            try
            {
                var dynamoViewModel = _viewLoadedParams.DynamoWindow?.DataContext as DynamoViewModel;
                var workspace = dynamoViewModel?.Model?.CurrentWorkspace;
                if (workspace == null)
                {
                    details = LocalizationService.Get("NodeManipulator_WorkspaceNotFound");
                    return false;
                }

                var oldNode = workspace.Nodes.FirstOrDefault(n => n.GUID.ToString() == oldNodeId);
                if (oldNode == null)
                {
                    details = LocalizationService.Format("NodeManipulator_NodeNotFoundId", oldNodeId);
                    return false;
                }

                string oldNodeName = oldNode.Name;
                double x = oldNode.X;
                double y = oldNode.Y;

                // Store incoming connections
                var incomingConnections = new List<(string sourceId, int sourcePort, int targetPort)>();
                for (int i = 0; i < oldNode.InPorts.Count; i++)
                {
                    var port = oldNode.InPorts[i];
                    foreach (var connector in port.Connectors)
                    {
                        var sourceNode = connector.Start?.Owner;
                        if (sourceNode != null)
                        {
                            int sourcePortIndex = sourceNode.OutPorts.ToList().IndexOf(connector.Start);
                            incomingConnections.Add((sourceNode.GUID.ToString(), sourcePortIndex, i));
                        }
                    }
                }

                // Store outgoing connections
                var outgoingConnections = new List<(int sourcePort, string targetId, int targetPort)>();
                for (int i = 0; i < oldNode.OutPorts.Count; i++)
                {
                    var port = oldNode.OutPorts[i];
                    foreach (var connector in port.Connectors)
                    {
                        var targetNode = connector.End?.Owner;
                        if (targetNode != null)
                        {
                            int targetPortIndex = targetNode.InPorts.ToList().IndexOf(connector.End);
                            outgoingConnections.Add((i, targetNode.GUID.ToString(), targetPortIndex));
                        }
                    }
                }

                // Store existing node GUIDs before deletion and creation
                var existingNodeIds = new HashSet<string>(workspace.Nodes.Select(n => n.GUID.ToString()));
                existingNodeIds.Remove(oldNodeId); // Remove the one we're deleting

                // Delete old node
                var guids = new List<Guid> { oldNode.GUID };
                dynamoViewModel.ExecuteCommand(new DynamoModel.DeleteModelCommand(guids));

                // Create new node at same position
                if (!CreateNodeAtPosition(newNodeTypeName, x, y))
                {
                    details = LocalizationService.Format("NodeManipulator_NewNodeCreateFailed", newNodeTypeName);
                    return false;
                }

                // Find the newly created node (the one not in our previous set)
                var newNode = workspace.Nodes.FirstOrDefault(n => !existingNodeIds.Contains(n.GUID.ToString()));
                if (newNode == null)
                {
                    // Fallback: try last node
                    newNode = workspace.Nodes.LastOrDefault();
                }

                if (newNode == null)
                {
                    details = LocalizationService.Get("NodeManipulator_CreatedNodeNotFound");
                    return false;
                }

                // Restore incoming connections
                int restoredIncoming = 0;
                foreach (var conn in incomingConnections)
                {
                    if (conn.targetPort < newNode.InPorts.Count)
                    {
                        if (ConnectNodes(conn.sourceId, conn.sourcePort, newNode.GUID.ToString(), conn.targetPort))
                            restoredIncoming++;
                    }
                }

                // Restore outgoing connections
                int restoredOutgoing = 0;
                foreach (var conn in outgoingConnections)
                {
                    if (conn.sourcePort < newNode.OutPorts.Count)
                    {
                        if (ConnectNodes(newNode.GUID.ToString(), conn.sourcePort, conn.targetId, conn.targetPort))
                            restoredOutgoing++;
                    }
                }

                details = LocalizationService.Format("NodeManipulator_ReplaceSuccess", oldNodeName, newNodeTypeName, restoredIncoming, incomingConnections.Count, restoredOutgoing, outgoingConnections.Count);
                return true;
            }
            catch (Exception ex)
            {
                details = LocalizationService.Format("NodeManipulator_ReplaceError", ex.Message);
                return false;
            }
        }

        #endregion

        #region Action Type 5: DISCONNECT

        /// <summary>
        /// Disconnect two nodes
        /// </summary>
        private bool DisconnectNodes(string sourceNodeId, int sourcePortIndex, string targetNodeId, int targetPortIndex, out string details)
        {
            details = "";
            try
            {
                var dynamoViewModel = _viewLoadedParams.DynamoWindow?.DataContext as DynamoViewModel;
                var workspace = dynamoViewModel?.Model?.CurrentWorkspace;
                if (workspace == null)
                {
                    details = LocalizationService.Get("NodeManipulator_WorkspaceNotFound");
                    return false;
                }

                var sourceNode = workspace.Nodes.FirstOrDefault(n => n.GUID.ToString() == sourceNodeId);
                var targetNode = workspace.Nodes.FirstOrDefault(n => n.GUID.ToString() == targetNodeId);

                if (sourceNode == null || targetNode == null)
                {
                    details = LocalizationService.Get("NodeManipulator_NodeNotFound");
                    return false;
                }

                // Find the connector to delete
                var sourcePort = sourceNode.OutPorts.ElementAtOrDefault(sourcePortIndex);
                var targetPort = targetNode.InPorts.ElementAtOrDefault(targetPortIndex);

                if (sourcePort == null || targetPort == null)
                {
                    details = LocalizationService.Get("NodeManipulator_PortNotFound");
                    return false;
                }

                var connector = sourcePort.Connectors.FirstOrDefault(c => c.End == targetPort);
                if (connector == null)
                {
                    details = LocalizationService.Get("NodeManipulator_ConnectionNotFound");
                    return false;
                }

                // Delete the connector using DeleteModelCommand
                var guids = new List<Guid> { connector.GUID };
                dynamoViewModel.ExecuteCommand(new DynamoModel.DeleteModelCommand(guids));

                details = LocalizationService.Format("NodeManipulator_DisconnectSuccess", sourceNode.Name, targetNode.Name);
                return true;
            }
            catch (Exception ex)
            {
                details = LocalizationService.Format("NodeManipulator_DisconnectError", ex.Message);
                return false;
            }
        }

        #endregion

        #region Action Type 6: RECONNECT

        /// <summary>
        /// Change connection target from old node to new node
        /// </summary>
        private bool ReconnectNodes(string sourceNodeId, int sourcePortIndex, string oldTargetId, string newTargetId, int newTargetPortIndex, out string details)
        {
            details = "";
            try
            {
                // First disconnect from old target
                if (!DisconnectNodes(sourceNodeId, sourcePortIndex, oldTargetId, 0, out string disconnectDetails))
                {
                    // Try to find the actual port index
                    var dynamoViewModel = _viewLoadedParams.DynamoWindow?.DataContext as DynamoViewModel;
                    var workspace = dynamoViewModel?.Model?.CurrentWorkspace;
                    var sourceNode = workspace?.Nodes.FirstOrDefault(n => n.GUID.ToString() == sourceNodeId);
                    var oldTarget = workspace?.Nodes.FirstOrDefault(n => n.GUID.ToString() == oldTargetId);

                    if (sourceNode != null && oldTarget != null)
                    {
                        var sourcePort = sourceNode.OutPorts.ElementAtOrDefault(sourcePortIndex);
                        if (sourcePort != null)
                        {
                            foreach (var conn in sourcePort.Connectors.ToList())
                            {
                                if (conn.End?.Owner?.GUID.ToString() == oldTargetId)
                                {
                                    var guids = new List<Guid> { conn.GUID };
                                    dynamoViewModel.ExecuteCommand(new DynamoModel.DeleteModelCommand(guids));
                                    break;
                                }
                            }
                        }
                    }
                }

                // Then connect to new target
                if (!ConnectNodes(sourceNodeId, sourcePortIndex, newTargetId, newTargetPortIndex))
                {
                    details = LocalizationService.Get("NodeManipulator_ReconnectFailed");
                    return false;
                }

                details = LocalizationService.Get("NodeManipulator_ReconnectSuccess");
                return true;
            }
            catch (Exception ex)
            {
                details = LocalizationService.Format("NodeManipulator_ReconnectError", ex.Message);
                return false;
            }
        }

        #endregion

        #region Action Type 7: FIX_CODE

        /// <summary>
        /// Modify Python code by replacing a snippet
        /// </summary>
        private bool FixPythonCode(string nodeId, string oldCode, string newCode, out string details)
        {
            details = "";
            try
            {
                var dynamoViewModel = _viewLoadedParams.DynamoWindow?.DataContext as DynamoViewModel;
                var workspace = dynamoViewModel?.Model?.CurrentWorkspace;
                if (workspace == null)
                {
                    details = LocalizationService.Get("NodeManipulator_WorkspaceNotFound");
                    return false;
                }

                var node = workspace.Nodes.FirstOrDefault(n => n.GUID.ToString() == nodeId);
                if (node == null)
                {
                    details = LocalizationService.Format("NodeManipulator_NodeNotFoundId", nodeId);
                    return false;
                }

                // Try multiple property names for Python code (version compatibility)
                string[] codePropertyNames = { "Script", "Code", "ScriptContent", "PythonCode" };
                System.Reflection.PropertyInfo scriptProp = null;
                string currentCode = null;

                foreach (var propName in codePropertyNames)
                {
                    var prop = node.GetType().GetProperty(propName);
                    if (prop != null && prop.CanRead && prop.CanWrite)
                    {
                        var value = prop.GetValue(node);
                        if (value is string code && !string.IsNullOrEmpty(code))
                        {
                            scriptProp = prop;
                            currentCode = code;
                            break;
                        }
                    }
                }

                if (scriptProp == null || currentCode == null)
                {
                    details = LocalizationService.Get("NodeManipulator_NotPythonNode");
                    return false;
                }

                if (!currentCode.Contains(oldCode))
                {
                    details = LocalizationService.Get("NodeManipulator_CodePatchNotFound");
                    return false;
                }

                string fixedCode = currentCode.Replace(oldCode, newCode);
                scriptProp.SetValue(node, fixedCode);

                // Truncate for display if too long
                string displayOld = oldCode.Length > 30 ? oldCode.Substring(0, 30) + "..." : oldCode;
                string displayNew = newCode.Length > 30 ? newCode.Substring(0, 30) + "..." : newCode;
                details = LocalizationService.Format("NodeManipulator_CodeFixSuccess", displayOld, displayNew);
                return true;
            }
            catch (Exception ex)
            {
                details = LocalizationService.Format("NodeManipulator_CodeFixError", ex.Message);
                return false;
            }
        }

        #endregion

        #region Action Type 8: REPLACE_CODE

        /// <summary>
        /// Replace entire Python code
        /// </summary>
        private bool ReplacePythonCode(string nodeId, string newCode, out string details)
        {
            details = "";
            try
            {
                var dynamoViewModel = _viewLoadedParams.DynamoWindow?.DataContext as DynamoViewModel;
                var workspace = dynamoViewModel?.Model?.CurrentWorkspace;
                if (workspace == null)
                {
                    details = LocalizationService.Get("NodeManipulator_WorkspaceNotFound");
                    return false;
                }

                var node = workspace.Nodes.FirstOrDefault(n => n.GUID.ToString() == nodeId);
                if (node == null)
                {
                    details = LocalizationService.Format("NodeManipulator_NodeNotFoundId", nodeId);
                    return false;
                }

                // Try multiple property names for Python code (version compatibility)
                string[] codePropertyNames = { "Script", "Code", "ScriptContent", "PythonCode" };
                System.Reflection.PropertyInfo scriptProp = null;

                foreach (var propName in codePropertyNames)
                {
                    var prop = node.GetType().GetProperty(propName);
                    if (prop != null && prop.CanWrite)
                    {
                        // For write, we just need CanWrite - don't require existing value
                        scriptProp = prop;
                        break;
                    }
                }

                if (scriptProp == null)
                {
                    details = LocalizationService.Get("NodeManipulator_NotPythonNode");
                    return false;
                }

                scriptProp.SetValue(node, newCode);

                int lineCount = newCode.Split('
').Length;
                details = LocalizationService.Format("NodeManipulator_CodeReplaceSuccess", lineCount);
                return true;
            }
            catch (Exception ex)
            {
                details = LocalizationService.Format("NodeManipulator_CodeReplaceError", ex.Message);
                return false;
            }
        }

        #endregion

        #region Action Type 9: SET_VALUE

        /// <summary>
        /// Set value for Code Block or Number Slider
        /// </summary>
        private bool SetNodeValue(string nodeId, string newValue, out string details)
        {
            details = "";
            try
            {
                var dynamoViewModel = _viewLoadedParams.DynamoWindow?.DataContext as DynamoViewModel;
                var workspace = dynamoViewModel?.Model?.CurrentWorkspace;
                if (workspace == null)
                {
                    details = LocalizationService.Get("NodeManipulator_WorkspaceNotFound");
                    return false;
                }

                var node = workspace.Nodes.FirstOrDefault(n => n.GUID.ToString() == nodeId);
                if (node == null)
                {
                    details = LocalizationService.Format("NodeManipulator_NodeNotFoundId", nodeId);
                    return false;
                }

                string nodeTypeName = node.GetType().Name;

                // Try Code Block first (CodeBlockNodeModel)
                var codeProp = node.GetType().GetProperty("Code");
                if (codeProp != null && codeProp.CanWrite)
                {
                    // Code Block requires semicolon, but avoid double semicolon
                    string codeValue = newValue.TrimEnd();
                    if (!codeValue.EndsWith(";"))
                    {
                        codeValue += ";";
                    }
                    codeProp.SetValue(node, codeValue);
                    details = LocalizationService.Format("NodeManipulator_CodeBlockValueChanged", newValue);
                    return true;
                }

                // Try Number Slider (DoubleSlider, IntegerSlider)
                var valueProp = node.GetType().GetProperty("Value");
                if (valueProp != null && valueProp.CanWrite)
                {
                    // Check property type and convert accordingly
                    var propType = valueProp.PropertyType;

                    if (propType == typeof(double))
                    {
                        if (double.TryParse(newValue, out double doubleVal))
                        {
                            valueProp.SetValue(node, doubleVal);
                            details = LocalizationService.Format("NodeManipulator_ValueChangedNum", newValue);
                            return true;
                        }
                    }
                    else if (propType == typeof(int))
                    {
                        if (int.TryParse(newValue, out int intVal))
                        {
                            valueProp.SetValue(node, intVal);
                            details = LocalizationService.Format("NodeManipulator_ValueChangedNum", newValue);
                            return true;
                        }
                    }
                    else if (propType == typeof(string))
                    {
                        valueProp.SetValue(node, newValue);
                        details = LocalizationService.Format("NodeManipulator_ValueChangedStr", newValue);
                        return true;
                    }

                    details = LocalizationService.Format("NodeManipulator_ValueTypeMismatch", propType.Name);
                    return false;
                }

                // Try String Input node
                var inputValueProp = node.GetType().GetProperty("InputValue");
                if (inputValueProp != null && inputValueProp.CanWrite)
                {
                    inputValueProp.SetValue(node, newValue);
                    details = LocalizationService.Format("NodeManipulator_InputValueChanged", newValue);
                    return true;
                }

                // Try generic Text property (for some input nodes)
                var textProp = node.GetType().GetProperty("Text");
                if (textProp != null && textProp.CanWrite)
                {
                    textProp.SetValue(node, newValue);
                    details = LocalizationService.Format("NodeManipulator_TextChanged", newValue);
                    return true;
                }

                details = LocalizationService.Format("NodeManipulator_NotValueNode", nodeTypeName);
                return false;
            }
            catch (Exception ex)
            {
                details = LocalizationService.Format("NodeManipulator_SetValueError", ex.Message);
                return false;
            }
        }

        #endregion

        #region Action Type 10: SET_LACING

        /// <summary>
        /// Change node lacing mode
        /// </summary>
        private bool SetNodeLacing(string nodeId, string lacingMode, out string details)
        {
            details = "";
            try
            {
                var dynamoViewModel = _viewLoadedParams.DynamoWindow?.DataContext as DynamoViewModel;
                var workspace = dynamoViewModel?.Model?.CurrentWorkspace;
                if (workspace == null)
                {
                    details = LocalizationService.Get("NodeManipulator_WorkspaceNotFound");
                    return false;
                }

                var node = workspace.Nodes.FirstOrDefault(n => n.GUID.ToString() == nodeId);
                if (node == null)
                {
                    details = LocalizationService.Format("NodeManipulator_NodeNotFoundId", nodeId);
                    return false;
                }

                // Parse lacing mode
                LacingStrategy lacing;
                switch (lacingMode.ToLower())
                {
                    case "auto":
                        lacing = LacingStrategy.Auto;
                        break;
                    case "shortest":
                        lacing = LacingStrategy.Shortest;
                        break;
                    case "longest":
                        lacing = LacingStrategy.Longest;
                        break;
                    case "crossproduct":
                        lacing = LacingStrategy.CrossProduct;
                        break;
                    default:
                        details = LocalizationService.Format("NodeManipulator_UnknownLacingMode", lacingMode);
                        return false;
                }

                // Set lacing via UpdateValue command
                dynamoViewModel.ExecuteCommand(new DynamoModel.UpdateModelValueCommand(
                    Guid.Empty, node.GUID, "ArgumentLacing", lacing.ToString()));

                details = LocalizationService.Format("NodeManipulator_LacingChanged", lacingMode);
                return true;
            }
            catch (Exception ex)
            {
                details = LocalizationService.Format("NodeManipulator_LacingError", ex.Message);
                return false;
            }
        }

        #endregion

        #region Action Type 11 & 12: GROUP_NODES, ADD_NOTE (Existing Methods)

        /// <summary>
        /// Section 3.1: Auto Grouping
        /// Groups nodes by functional context (input/processing/output)
        /// </summary>
        public bool CreateGroup(List<string> nodeIds, string title, string description = null, string colorHex = "#FFFFE0B2")
        {
            try
            {
                var dynamoViewModel = _viewLoadedParams.DynamoWindow?.DataContext as DynamoViewModel;
                var workspace = dynamoViewModel?.Model?.CurrentWorkspace;
                if (workspace == null) return false;

                // Find nodes by GUID
                var nodesToGroup = workspace.Nodes
                    .Where(n => nodeIds.Contains(n.GUID.ToString()))
                    .ToList();

                if (!nodesToGroup.Any()) return false;

                // Calculate bounding box
                double minX = nodesToGroup.Min(n => n.X);
                double minY = nodesToGroup.Min(n => n.Y);
                double maxX = nodesToGroup.Max(n => n.X) + 200; // Approximate node width
                double maxY = nodesToGroup.Max(n => n.Y) + 100; // Approximate node height

                // Create annotation (group) - use NodeModel type for Dynamo 3.6.1 compatibility
                var annotation = new AnnotationModel(
                    nodesToGroup.Cast<NodeModel>(),
                    new List<NoteModel>())
                {
                    AnnotationText = title
                };

                // Try to set background color via reflection (varies by Dynamo version)
                try
                {
                    var bgProperty = annotation.GetType().GetProperty("Background");
                    if (bgProperty != null && bgProperty.CanWrite)
                    {
                        bgProperty.SetValue(annotation, colorHex);
                    }
                }
                catch { }

                // Try to set description via reflection (DescriptionText property)
                if (!string.IsNullOrEmpty(description))
                {
                    try
                    {
                        var descProperty = annotation.GetType().GetProperty("DescriptionText");
                        if (descProperty != null && descProperty.CanWrite)
                        {
                            descProperty.SetValue(annotation, description);
                        }
                    }
                    catch { }
                }

                // Calculate position for annotation
                double left = minX - 10;
                double top = minY - 40;

                // Use Command pattern for Dynamo 3.6.1+ compatibility
                dynamoViewModel.ExecuteCommand(new DynamoModel.CreateAnnotationCommand(
                    annotation.GUID,
                    annotation.AnnotationText,
                    left,
                    top,
                    false));

                return true;
            }
            catch (Exception ex)
            {
                Log($"CreateGroup Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Zoom to fit all nodes in view, then center on specific node if provided
        /// </summary>
        public bool ZoomToFit(string nodeId = null)
        {
            try
            {
                var dynamoViewModel = _viewLoadedParams.DynamoWindow?.DataContext as DynamoViewModel;
                if (dynamoViewModel == null) return false;

                // Execute FitView command to show all nodes
                if (dynamoViewModel.FitViewCommand?.CanExecute(null) == true)
                {
                    dynamoViewModel.FitViewCommand.Execute(null);
                    Log("ZoomToFit: FitViewCommand executed");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Log($"ZoomToFit Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Section 3.2: Smart Annotation
        /// Adds explanatory notes at specified positions
        /// </summary>
        public bool AddNote(string text, double x, double y)
        {
            try
            {
                var dynamoViewModel = _viewLoadedParams.DynamoWindow?.DataContext as DynamoViewModel;
                var workspace = dynamoViewModel?.Model?.CurrentWorkspace;
                if (workspace == null) return false;

                // Create note model
                var note = new NoteModel(x, y - 50, text, Guid.NewGuid()); // Position above target

                // Use Command pattern for Dynamo 3.6.1+ compatibility
                dynamoViewModel.ExecuteCommand(new DynamoModel.CreateNoteCommand(
                    note.GUID,
                    note.Text,
                    note.X,
                    note.Y,
                    false));

                return true;
            }
            catch (Exception ex)
            {
                Log($"AddNote Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Section 3.3: QA Logic - Find empty space for new node
        /// Calculates position avoiding existing nodes using bounding box
        /// </summary>
        public (double x, double y) FindEmptySpace(double nodeWidth = 200, double nodeHeight = 150, double padding = 50)
        {
            try
            {
                var dynamoViewModel = _viewLoadedParams.DynamoWindow?.DataContext as DynamoViewModel;
                var workspace = dynamoViewModel?.Model?.CurrentWorkspace;
                
                if (workspace == null || !workspace.Nodes.Any())
                    return (100, 100);

                // Get all node bounding boxes
                var boundingBoxes = workspace.Nodes.Select(n => new
                {
                    Left = n.X,
                    Top = n.Y,
                    Right = n.X + nodeWidth,
                    Bottom = n.Y + nodeHeight
                }).ToList();

                // Strategy 1: Place to the right of rightmost node
                double maxRight = boundingBoxes.Max(b => b.Right);
                double avgY = boundingBoxes.Average(b => b.Top);
                
                return (maxRight + padding, avgY);
            }
            catch
            {
                return (100, 100);
            }
        }

        /// <summary>
        /// Create a node at specified position
        /// </summary>
        public bool CreateNodeAtPosition(string nodeTypeName, double x, double y)
        {
            try
            {
                var dynamoViewModel = _viewLoadedParams.DynamoWindow?.DataContext as DynamoViewModel;
                if (dynamoViewModel == null)
                {
                    Log("DynamoViewModel is null");
                    return false;
                }

                Log($"[CreateNode] Searching for: '{nodeTypeName}'");

                // Strategy 1: Try exact Type name match first (for precise type names)
                Type nodeType = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        nodeType = assembly.GetTypes()
                            .FirstOrDefault(t => t.Name == nodeTypeName && 
                                                 typeof(NodeModel).IsAssignableFrom(t) && 
                                                 !t.IsAbstract);
                        if (nodeType != null)
                        {
                            Log($"[CreateNode] Found by Type name: {nodeType.FullName}");
                            break;
                        }
                    }
                    catch { continue; }
                }

                // Strategy 2: If not found, try Dynamo's search library (for user-friendly names)
                if (nodeType == null)
                {
                    Log($"[CreateNode] Type name not found, trying search library...");
                    
                    // Use reflection to access SearchViewModel (version compatibility)
                    var searchViewModelProp = dynamoViewModel.GetType().GetProperty("SearchViewModel");
                    var searchViewModel = searchViewModelProp?.GetValue(dynamoViewModel);
                    
                    if (searchViewModel != null)
                    {
                        // Access SearchDictionary.entries via reflection
                        var searchDictProp = searchViewModel.GetType().GetProperty("SearchDictionary");
                        var searchDict = searchDictProp?.GetValue(searchViewModel);
                        
                        if (searchDict != null)
                        {
                            var entriesProp = searchDict.GetType().GetProperty("entries");
                            var entries = entriesProp?.GetValue(searchDict) as System.Collections.IEnumerable;
                            
                            if (entries != null)
                            {
                                var searchResults = new List<dynamic>();
                                foreach (var entry in entries)
                                {
                                    var nameProp = entry.GetType().GetProperty("Name");
                                    var name = nameProp?.GetValue(entry) as string;
                                    
                                    if (name != null && 
                                        (name.Equals(nodeTypeName, StringComparison.OrdinalIgnoreCase) ||
                                         name.IndexOf(nodeTypeName, StringComparison.OrdinalIgnoreCase) >= 0))
                                    {
                                        searchResults.Add(entry);
                                    }
                                }
                                
                                // Sort by name length (prefer shorter/exact matches)
                                searchResults = searchResults.OrderBy(e => 
                                {
                                    var nameProp = e.GetType().GetProperty("Name");
                                    var name = nameProp?.GetValue(e) as string;
                                    return name?.Length ?? int.MaxValue;
                                }).ToList();

                                Log($"[CreateNode] Found {searchResults.Count} search results");

                                if (searchResults.Any())
                                {
                                    var bestMatch = searchResults.First();
                                    
                                    // Get Name and CreationName via reflection
                                    var bestMatchNameProp = bestMatch.GetType().GetProperty("Name");
                                    var bestMatchName = bestMatchNameProp?.GetValue(bestMatch) as string;
                                    
                                    var creationNameProp = bestMatch.GetType().GetProperty("CreationName");
                                    var creationName = creationNameProp?.GetValue(bestMatch) as string;
                                    
                                    Log($"[CreateNode] Best match: '{bestMatchName}' (CreationName: {creationName})");

                                    // Try to get the Type from CreationName
                                    if (!string.IsNullOrEmpty(creationName))
                                    {
                                        // CreationName format: "Namespace.TypeName" or just "TypeName"
                                        string typeName = creationName.Contains(".") 
                                            ? creationName.Substring(creationName.LastIndexOf('.') + 1)
                                            : creationName;

                                        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                                        {
                                            try
                                            {
                                                nodeType = assembly.GetTypes()
                                                    .FirstOrDefault(t => (t.Name == typeName || t.FullName == creationName) && 
                                                                         typeof(NodeModel).IsAssignableFrom(t) && 
                                                                         !t.IsAbstract);
                                                if (nodeType != null)
                                                {
                                                    Log($"[CreateNode] Found by CreationName: {nodeType.FullName}");
                                                    break;
                                                }
                                            }
                                            catch { continue; }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (nodeType == null)
                {
                    Log($"[CreateNode] FAILED - Node type not found for: '{nodeTypeName}'");
                    return false;
                }

                var node = Activator.CreateInstance(nodeType) as NodeModel;
                if (node == null)
                {
                    Log($"[CreateNode] FAILED - Could not create instance of {nodeType.FullName}");
                    return false;
                }

                dynamoViewModel.ExecuteCommand(new DynamoModel.CreateNodeCommand(node, x, y, false, false));
                Log($"[CreateNode] SUCCESS - Created {nodeType.Name} at ({x}, {y})");
                return true;
            }
            catch (Exception ex)
            {
                Log($"[CreateNode] ERROR: {ex.Message}
StackTrace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Connect two nodes via wire
        /// </summary>
        public bool ConnectNodes(string sourceNodeId, int sourcePortIndex, string targetNodeId, int targetPortIndex)
        {
            try
            {
                var dynamoViewModel = _viewLoadedParams.DynamoWindow?.DataContext as DynamoViewModel;
                var workspace = dynamoViewModel?.Model?.CurrentWorkspace;
                if (workspace == null) return false;

                var sourceNode = workspace.Nodes.FirstOrDefault(n => n.GUID.ToString() == sourceNodeId);
                var targetNode = workspace.Nodes.FirstOrDefault(n => n.GUID.ToString() == targetNodeId);

                if (sourceNode == null || targetNode == null) return false;

                var sourcePort = sourceNode.OutPorts.ElementAtOrDefault(sourcePortIndex);
                var targetPort = targetNode.InPorts.ElementAtOrDefault(targetPortIndex);

                if (sourcePort == null || targetPort == null) return false;

                // Use string GUID and Dynamo.Graph.Nodes.PortType for Dynamo 3.6.1+ compatibility
                dynamoViewModel.ExecuteCommand(new DynamoModel.MakeConnectionCommand(
                    sourceNode.GUID.ToString(), sourcePortIndex, Dynamo.Graph.Nodes.PortType.Output,
                    DynamoModel.MakeConnectionCommand.Mode.Begin));

                dynamoViewModel.ExecuteCommand(new DynamoModel.MakeConnectionCommand(
                    targetNode.GUID.ToString(), targetPortIndex, Dynamo.Graph.Nodes.PortType.Input,
                    DynamoModel.MakeConnectionCommand.Mode.End));

                return true;
            }
            catch (Exception ex)
            {
                Log($"ConnectNodes Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Auto-group nodes by analyzing their topology
        /// </summary>
        public void AutoGroupByTopology()
        {
            try
            {
                var dynamoViewModel = _viewLoadedParams.DynamoWindow?.DataContext as DynamoViewModel;
                var workspace = dynamoViewModel?.Model?.CurrentWorkspace;
                if (workspace == null) return;

                // Identify input nodes (no incoming connections)
                var inputNodes = workspace.Nodes
                    .Where(n => !n.InPorts.Any(p => p.Connectors.Any()))
                    .Select(n => n.GUID.ToString())
                    .ToList();

                // Identify output nodes (no outgoing connections)
                var outputNodes = workspace.Nodes
                    .Where(n => !n.OutPorts.Any(p => p.Connectors.Any()))
                    .Select(n => n.GUID.ToString())
                    .ToList();

                // Processing nodes are everything else
                var processingNodes = workspace.Nodes
                    .Where(n => !inputNodes.Contains(n.GUID.ToString()) && 
                               !outputNodes.Contains(n.GUID.ToString()))
                    .Select(n => n.GUID.ToString())
                    .ToList();

                // Create groups
                if (inputNodes.Count > 1)
                    CreateGroup(inputNodes, LocalizationService.Get("NodeManipulator_GroupLabel_Input"), "#4ADE80");

                if (processingNodes.Count > 1)
                    CreateGroup(processingNodes, LocalizationService.Get("NodeManipulator_GroupLabel_Processing"), "#007ACC");

                if (outputNodes.Count > 1)
                    CreateGroup(outputNodes, LocalizationService.Get("NodeManipulator_GroupLabel_Output"), "#FFD93D");
            }
            catch (Exception ex)
            {
                Log($"AutoGroupByTopology Error: {ex.Message}");
            }
        }

        private void Log(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "bibim_debug.txt");
                System.IO.File.AppendAllText(path,
                    DateTime.Now.ToString() + " [NodeManipulator]: " + message + Environment.NewLine);
            }
            catch { }
        }

        #endregion

        #region Port manipulation

        /// <summary>
        /// Adjusts the number of input ports on a Python node to match <paramref name="inputCount"/>.
        /// Tries three strategies in order:
        ///   1. Reflection-based AddInput / RemoveInput methods (preferred for PythonNodeModel)
        ///   2. VariableInputPorts + PortModel constructor injection
        ///   3. InPortData collection rebuild
        /// Returns true when node.InPorts.Count == inputCount after the operation.
        /// </summary>
        public static bool AddInputPortsToPythonNode(NodeModel node, int inputCount)
        {
            try
            {
                // Declare types that may be needed by multiple strategies
                Type portModelType = null;
                Type portDataType = null;
                Type portTypeEnum = null;

                // Log all available methods on the node for debugging (including inherited)
                Logger.Log("NodeManipulator", $"=== Analyzing node type: {node.GetType().FullName} ===");
                var allMethods = node.GetType().GetMethods(System.Reflection.BindingFlags.Public |
                                                           System.Reflection.BindingFlags.Instance);
                var inputRelatedMethods = allMethods.Where(m => m.Name.Contains("Input") || m.Name.Contains("Port")).ToList();
                Logger.Log("NodeManipulator", $"Input/Port related methods: {string.Join(", ", inputRelatedMethods.Select(m => m.Name))}");

                // Log all properties
                var props = node.GetType().GetProperties(System.Reflection.BindingFlags.Public |
                                                         System.Reflection.BindingFlags.Instance);
                var portRelatedProps = props.Where(p => p.Name.Contains("Port") || p.Name.Contains("Input")).ToList();
                Logger.Log("NodeManipulator", $"Port related properties: {string.Join(", ", portRelatedProps.Select(p => $"{p.Name}({p.PropertyType.Name})"))}");

                // Strategy 1: Try using AddInput method (preferred for Python nodes)
                var addInputMethod = node.GetType().GetMethod("AddInput");
                var removeInputMethod = node.GetType().GetMethod("RemoveInput");

                Logger.Log("NodeManipulator", $"AddInput method found: {addInputMethod != null}");
                Logger.Log("NodeManipulator", $"RemoveInput method found: {removeInputMethod != null}");

                if (addInputMethod != null)
                {
                    Logger.Log("NodeManipulator", "Using AddInput method");

                    int currentCount = node.InPorts.Count;
                    Logger.Log("NodeManipulator", $"Current inputs: {currentCount}, Required: {inputCount}");

                    while (node.InPorts.Count < inputCount)
                    {
                        addInputMethod.Invoke(node, null);
                        Logger.Log("NodeManipulator", $"Added input, now have: {node.InPorts.Count}");
                    }

                    while (node.InPorts.Count > inputCount && removeInputMethod != null)
                    {
                        removeInputMethod.Invoke(node, null);
                    }

                    return true;
                }

                // Strategy 2: Use VariableInputPorts and InPorts ObservableCollection (for PythonNode)
                Logger.Log("NodeManipulator", "Trying Strategy 2: VariableInputPorts + InPorts collection");

                var variableInputPortsProp = node.GetType().GetProperty("VariableInputPorts");
                var inPortsProp = node.GetType().GetProperty("InPorts");

                if (variableInputPortsProp != null && inPortsProp != null)
                {
                    Logger.Log("NodeManipulator", "Found VariableInputPorts and InPorts properties");

                    var inPorts = inPortsProp.GetValue(node) as System.Collections.IList;
                    if (inPorts != null)
                    {
                        Logger.Log("NodeManipulator", $"Got InPorts collection, current count: {inPorts.Count}");

                        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            try
                            {
                                var types = assembly.GetTypes();

                                if (portModelType == null)
                                    portModelType = types.FirstOrDefault(t => t.Name == "PortModel" &&
                                        t.Namespace != null && t.Namespace.Contains("Dynamo"));

                                if (portDataType == null)
                                    portDataType = types.FirstOrDefault(t => t.Name == "PortData" &&
                                        t.Namespace != null && t.Namespace.Contains("Dynamo"));

                                if (portTypeEnum == null)
                                    portTypeEnum = types.FirstOrDefault(t => t.Name == "PortType" &&
                                        t.IsEnum && t.Namespace != null && t.Namespace.Contains("Dynamo"));

                                if (portModelType != null && portDataType != null && portTypeEnum != null)
                                    break;
                            }
                            catch { continue; }
                        }

                        if (portModelType != null && portDataType != null && portTypeEnum != null)
                        {
                            Logger.Log("NodeManipulator", $"Found types - PortModel: {portModelType.FullName}, PortData: {portDataType.FullName}, PortType: {portTypeEnum.FullName}");

                            var portTypeInput = Enum.Parse(portTypeEnum, "Input");
                            Logger.Log("NodeManipulator", $"PortType.Input resolved");

                            int currentCount = inPorts.Count;
                            Logger.Log("NodeManipulator", $"Current: {currentCount}, Required: {inputCount}");

                            for (int i = currentCount; i < inputCount; i++)
                            {
                                try
                                {
                                    var portData = Activator.CreateInstance(portDataType, $"IN[{i}]", $"Input {i}");
                                    Logger.Log("NodeManipulator", $"Created PortData for port {i}");

                                    var constructor = portModelType.GetConstructor(new Type[] {
                                        portTypeEnum,
                                        typeof(NodeModel),
                                        portDataType
                                    });

                                    if (constructor != null)
                                    {
                                        var portModel = constructor.Invoke(new object[] {
                                            portTypeInput,
                                            node,
                                            portData
                                        });
                                        inPorts.Add(portModel);
                                        Logger.Log("NodeManipulator", $"Added port {i} successfully");
                                    }
                                    else
                                    {
                                        Logger.Log("NodeManipulator", "PortModel constructor not found");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.Log("NodeManipulator", $"Failed to create port {i}: {ex.Message}");
                                }
                            }

                            var refreshMethod = node.GetType().GetMethod("RegisterAllPorts") ??
                                               node.GetType().GetMethod("RegisterInputPorts");
                            if (refreshMethod != null)
                            {
                                refreshMethod.Invoke(node, null);
                                Logger.Log("NodeManipulator", $"Called {refreshMethod.Name}");
                            }

                            Logger.Log("NodeManipulator", $"Final InPorts.Count: {node.InPorts.Count}");
                            return node.InPorts.Count == inputCount;
                        }
                        else
                        {
                            Logger.Log("NodeManipulator", $"Types not found - PortModel: {portModelType != null}, PortData: {portDataType != null}, PortType: {portTypeEnum != null}");
                        }
                    }
                    else
                    {
                        Logger.Log("NodeManipulator", "InPorts collection is null");
                    }
                }
                else
                {
                    Logger.Log("NodeManipulator", $"VariableInputPorts found: {variableInputPortsProp != null}, InPorts found: {inPortsProp != null}");
                }

                // Strategy 3: Modify InPortData (fallback for nodes without AddInput)
                Logger.Log("NodeManipulator", "Trying Strategy 3: InPortData method");

                var inPortDataProp = node.GetType().GetProperty("InPortData");
                if (inPortDataProp == null)
                {
                    Logger.Log("NodeManipulator", "InPortData property not found");
                    return false;
                }

                var inPortData = inPortDataProp.GetValue(node) as System.Collections.IList;
                if (inPortData == null)
                {
                    Logger.Log("NodeManipulator", "InPortData is null");
                    return false;
                }

                if (portDataType == null)
                {
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            portDataType = assembly.GetTypes()
                                .FirstOrDefault(t => t.Name == "PortData" && t.Namespace != null &&
                                                   (t.Namespace.Contains("Dynamo.Graph") || t.Namespace.Contains("Dynamo")));
                            if (portDataType != null) break;
                        }
                        catch { continue; }
                    }
                }

                if (portDataType == null)
                {
                    Logger.Log("NodeManipulator", "PortData type not found");
                    return false;
                }

                inPortData.Clear();
                for (int i = 0; i < inputCount; i++)
                {
                    var portData = Activator.CreateInstance(portDataType, $"IN[{i}]", $"Input {i}");
                    inPortData.Add(portData);
                }

                var registerMethod = node.GetType().GetMethod("RegisterAllPorts");
                if (registerMethod != null)
                {
                    registerMethod.Invoke(node, null);
                    Logger.Log("NodeManipulator", $"RegisterAllPorts called, InPorts.Count: {node.InPorts.Count}");
                }

                return node.InPorts.Count == inputCount;
            }
            catch (Exception ex)
            {
                Logger.Log("NodeManipulator", $"Failed to add input ports: {ex.Message}
StackTrace: {ex.StackTrace}");
                return false;
            }
        }

        #endregion
    }

    /// <summary>
    /// Result of executing an analysis action
    /// </summary>
    public class ActionExecutionResult
    {
        public string ActionId { get; set; }
        public ActionType ActionType { get; set; }
        public string DisplayText { get; set; }
        public bool Success { get; set; }
        public string Details { get; set; }

        /// <summary>
        /// Generate HTML report for this action result
        /// </summary>
        public string ToHtmlReport()
        {
            string statusClass = Success ? "success" : "error";
            string statusIcon = Success ? "[OK]" : "[FAIL]";
            
            return $@"
<div class='action-result {statusClass}'>
    <span class='status'>{statusIcon}</span>
    <strong>{DisplayText}</strong>
    <p>{Details}</p>
</div>";
        }
    }
}
