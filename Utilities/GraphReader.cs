using System;
using System.Collections.Generic;
using System.Linq;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Connectors;
using Dynamo.Graph.Workspaces;
using Dynamo.Models;
using Dynamo.ViewModels;
using Dynamo.Wpf.Extensions;
#if NET48
using Newtonsoft.Json;
#else
using System.Text.Json;
using System.Text.Json.Serialization;
#endif

namespace BIBIM_MVP
{
    /// <summary>
    /// Section 2.1: Data Extraction Logic
    /// Extracts comprehensive node data from Dynamo workspace for AI analysis
    /// </summary>
    public class GraphReader
    {
        private readonly ViewLoadedParams _viewLoadedParams;

        private static void LogGraphReader(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[BIBIM-GraphReader] {message}");
            Logger.Log("GraphReader", $"[GRAPH_ANALYSIS] {message}");
        }

        public GraphReader(ViewLoadedParams viewLoadedParams)
        {
            _viewLoadedParams = viewLoadedParams;
        }

        /// <summary>
        /// Extract all node data from current workspace as JSON
        /// </summary>
        public GraphAnalysisData ExtractGraphData()
        {
            var data = new GraphAnalysisData();
            LogGraphReader("ExtractGraphData 시작");
            
            try
            {
                var dynamoViewModel = _viewLoadedParams.DynamoWindow?.DataContext as DynamoViewModel;
                if (dynamoViewModel == null)
                {
                    data.Error = LocalizationService.Get("ViewModel_DynamoViewModelNotFound");
                    LogGraphReader(data.Error);
                    return data;
                }

                var workspace = dynamoViewModel.Model?.CurrentWorkspace;
                if (workspace == null)
                {
                    data.Error = LocalizationService.Get("NodeManipulator_WorkspaceNotFound");
                    LogGraphReader(data.Error);
                    return data;
                }

                data.WorkspaceName = workspace.Name ?? "Untitled";
                data.NodeCount = workspace.Nodes.Count();
                data.ConnectorCount = workspace.Connectors.Count();
                LogGraphReader($"워크스페이스: {data.WorkspaceName}, Nodes: {data.NodeCount}, Connectors: {data.ConnectorCount}");
                
                // Add environment info from config
                var config = ConfigService.GetRagConfig();
                data.Environment = new EnvironmentInfo
                {
                    RevitVersion = config.RevitVersion,
                    DynamoVersion = config.DynamoVersion,
                    PythonEngine = config.RevitVersion == "2022" ? "IronPython 2.7" : "CPython 3.x"
                };
                LogGraphReader($"환경 정보 - Revit: {data.Environment.RevitVersion}, Dynamo: {data.Environment.DynamoVersion}, Python: {data.Environment.PythonEngine}");

                // Extract nodes
                foreach (var node in workspace.Nodes)
                {
                    var nodeData = ExtractNodeData(node);
                    data.Nodes.Add(nodeData);
                }

                // Extract connectors (wires)
                foreach (var connector in workspace.Connectors)
                {
                    var wireData = ExtractWireData(connector);
                    data.Wires.Add(wireData);
                }

                // Identify disconnected input ports
                data.DisconnectedInputs = FindDisconnectedInputs(workspace);

                // Extract groups/annotations if any
                foreach (var annotation in workspace.Annotations)
                {
                    data.Groups.Add(new GroupData
                    {
                        Id = annotation.GUID.ToString(),
                        Title = annotation.AnnotationText ?? "",
                        NodeIds = annotation.Nodes.Select(n => n.GUID.ToString()).ToList()
                    });
                }

                int errorNodeCount = data.Nodes.Count(n => n.State == "Error");
                int warningNodeCount = data.Nodes.Count(n => n.State == "Warning" || n.State == "PersistentWarning");
                LogGraphReader($"추출 완료 - Nodes: {data.Nodes.Count}, Wires: {data.Wires.Count}, DisconnectedInputs: {data.DisconnectedInputs.Count}, Groups: {data.Groups.Count}, ErrorNodes: {errorNodeCount}, WarningNodes: {warningNodeCount}");
            }
            catch (Exception ex)
            {
                data.Error = LocalizationService.Format("Analysis_GraphReadError", ex.Message);
                LogGraphReader($"{data.Error}\n{ex.StackTrace}");
            }

            return data;
        }

        private NodeData ExtractNodeData(NodeModel node)
        {
            var nodeData = new NodeData
            {
                Id = node.GUID.ToString(),
                Name = node.Name ?? node.GetType().Name,
                OriginalName = node.GetType().Name,
                Category = node.Category ?? "Unknown",
                PositionX = node.X,
                PositionY = node.Y,
                State = GetNodeState(node),
                ErrorMessage = GetNodeErrorMessage(node)
            };

            // Extract Python code if this is a Python node
            nodeData.PythonCode = ExtractPythonCode(node);
            
            // Extract Code Block content
            nodeData.CodeBlockContent = ExtractCodeBlockContent(node);
            
            // Extract input values (Number, String, Boolean nodes)
            nodeData.InputValue = ExtractInputValue(node);
            
            // Extract Lacing mode
            nodeData.LacingMode = ExtractLacingMode(node);

            // Extract input port info
            foreach (var port in node.InPorts)
            {
                var portData = new PortData
                {
                    Name = port.Name ?? $"IN[{port.Index}]",
                    Index = port.Index,
                    IsConnected = port.Connectors.Any(),
                    DataType = port.ToolTip ?? "Unknown"
                };

                // Extract cached value if available (Section 2.1.5)
                if (node.State == ElementState.Active && port.Connectors.Any())
                {
                    portData.CachedValue = ExtractCachedValue(node, port.Index, true);
                }

                nodeData.InputPorts.Add(portData);
            }

            // Extract output port info
            foreach (var port in node.OutPorts)
            {
                var portData = new PortData
                {
                    Name = port.Name ?? $"OUT[{port.Index}]",
                    Index = port.Index,
                    IsConnected = port.Connectors.Any(),
                    DataType = port.ToolTip ?? "Unknown"
                };

                // Extract cached output value
                if (node.State == ElementState.Active)
                {
                    portData.CachedValue = ExtractCachedValue(node, port.Index, false);
                }

                nodeData.OutputPorts.Add(portData);
            }

            return nodeData;
        }

        /// <summary>
        /// Extract Code Block content (DesignScript code)
        /// </summary>
        private string ExtractCodeBlockContent(NodeModel node)
        {
            var nodeType = node.GetType();
            var typeName = nodeType.Name;
            
            // Check if this is a Code Block node
            if (!typeName.Contains("CodeBlock") && !typeName.Contains("CBN"))
                return null;
            
            // Try common property names for Code Block content
            string[] codePropertyNames = { "Code", "CodeText", "Definition", "Expression" };
            
            foreach (var propName in codePropertyNames)
            {
                var prop = nodeType.GetProperty(propName);
                if (prop != null && prop.CanRead)
                {
                    try
                    {
                        var value = prop.GetValue(node);
                        if (value is string code && !string.IsNullOrEmpty(code))
                        {
                            return code;
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        /// <summary>
        /// Extract input values from Number, String, Boolean, Integer nodes
        /// </summary>
        private string ExtractInputValue(NodeModel node)
        {
            var nodeType = node.GetType();
            var typeName = nodeType.Name;
            
            // Check if this is an input node type
            bool isInputNode = typeName.Contains("Number") || 
                               typeName.Contains("String") || 
                               typeName.Contains("Boolean") ||
                               typeName.Contains("Integer") ||
                               typeName.Contains("DoubleInput") ||
                               typeName.Contains("StringInput") ||
                               typeName.Contains("BoolSelector");
            
            if (!isInputNode)
                return null;
            
            // Try common property names for input values
            string[] valuePropertyNames = { "Value", "InputValue", "NumberValue", "StringValue", "BoolValue" };
            
            foreach (var propName in valuePropertyNames)
            {
                var prop = nodeType.GetProperty(propName);
                if (prop != null && prop.CanRead)
                {
                    try
                    {
                        var value = prop.GetValue(node);
                        if (value != null)
                        {
                            return value.ToString();
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        /// <summary>
        /// Extract Lacing mode from node
        /// </summary>
        private string ExtractLacingMode(NodeModel node)
        {
            try
            {
                // Check if node supports lacing
                var lacingProp = node.GetType().GetProperty("ArgumentLacing");
                if (lacingProp != null && lacingProp.CanRead)
                {
                    var lacingValue = lacingProp.GetValue(node);
                    if (lacingValue != null)
                    {
                        return lacingValue.ToString();
                    }
                }
            }
            catch { }

            return null;
        }

        private string ExtractPythonCode(NodeModel node)
        {
            // Try to get Python code from various Python node types
            var nodeType = node.GetType();
            
            // Try common property names for Python code
            string[] codePropertyNames = { "Code", "Script", "ScriptContent", "PythonCode" };
            
            foreach (var propName in codePropertyNames)
            {
                var prop = nodeType.GetProperty(propName);
                if (prop != null && prop.CanRead)
                {
                    try
                    {
                        var value = prop.GetValue(node);
                        if (value is string code && !string.IsNullOrEmpty(code))
                        {
                            return code;
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        private string GetNodeState(NodeModel node)
        {
            switch (node.State)
            {
                case ElementState.Active:
                    return "Active";
                case ElementState.Warning:
                    return "Warning";
                case ElementState.Error:
                    return "Error";
                case ElementState.Dead:
                    return "Dead";
                case ElementState.PersistentWarning:
                    return "PersistentWarning";
                default:
                    return "Unknown";
            }
        }

        /// <summary>
        /// Get node error message with reflection for version compatibility
        /// </summary>
        private string GetNodeErrorMessage(NodeModel node)
        {
            try
            {
                // Use reflection for version compatibility (ToolTipText removed in Dynamo 3.6.1)
                var tooltipProp = node.GetType().GetProperty("ToolTipText");
                if (tooltipProp != null && tooltipProp.CanRead)
                {
                    var value = tooltipProp.GetValue(node);
                    return value?.ToString() ?? "";
                }
            }
            catch { }
            return "";
        }

        private CachedValueData ExtractCachedValue(NodeModel node, int portIndex, bool isInput)
        {
            try
            {
                // Try to get cached run data
                // This varies by Dynamo version, so we use reflection
                var cacheProperty = node.GetType().GetProperty("CachedValue");
                if (cacheProperty != null)
                {
                    var cachedValue = cacheProperty.GetValue(node);
                    if (cachedValue != null)
                    {
                        return OptimizeCachedValue(cachedValue);
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Section 2.1.5: Token optimization for cached values
        /// </summary>
        private CachedValueData OptimizeCachedValue(object value)
        {
            if (value == null)
            {
                return new CachedValueData { Type = "null", Value = "null" };
            }

            var type = value.GetType();

            // Simple values: return as-is
            if (value is string strVal)
            {
                // Truncate long strings
                if (strVal.Length > 200)
                    strVal = strVal.Substring(0, 200) + "...";
                return new CachedValueData { Type = "String", Value = strVal };
            }
            if (value is int || value is long || value is double || value is float || value is decimal)
            {
                return new CachedValueData { Type = "Number", Value = value.ToString() };
            }
            if (value is bool boolVal)
            {
                return new CachedValueData { Type = "Boolean", Value = boolVal.ToString() };
            }

            // Collections: return type and count
            if (value is System.Collections.IEnumerable enumerable && !(value is string))
            {
                int count = 0;
                string elementType = "Unknown";
                
                foreach (var item in enumerable)
                {
                    count++;
                    if (count == 1 && item != null)
                    {
                        elementType = GetSimpleTypeName(item.GetType());
                    }
                    if (count > 1000) break; // Limit counting
                }

                return new CachedValueData 
                { 
                    Type = "List", 
                    Value = $"{{\"elementType\": \"{elementType}\", \"count\": {count}}}" 
                };
            }

            // Complex objects: return type name only
            return new CachedValueData 
            { 
                Type = GetSimpleTypeName(type), 
                Value = $"{{\"type\": \"{GetSimpleTypeName(type)}\"}}" 
            };
        }

        private string GetSimpleTypeName(Type type)
        {
            var name = type.Name;
            
            // Common Revit types
            if (name.Contains("Wall")) return "Wall";
            if (name.Contains("Floor")) return "Floor";
            if (name.Contains("Door")) return "Door";
            if (name.Contains("Window")) return "Window";
            if (name.Contains("Room")) return "Room";
            if (name.Contains("Element")) return "Element";
            if (name.Contains("Geometry")) return "Geometry";
            if (name.Contains("Point")) return "Point";
            if (name.Contains("Line")) return "Line";
            if (name.Contains("Curve")) return "Curve";
            if (name.Contains("Surface")) return "Surface";
            if (name.Contains("Solid")) return "Solid";
            
            return name;
        }

        private WireData ExtractWireData(ConnectorModel connector)
        {
            return new WireData
            {
                SourceNodeId = connector.Start?.Owner?.GUID.ToString() ?? "",
                SourcePortIndex = connector.Start?.Index ?? -1,
                TargetNodeId = connector.End?.Owner?.GUID.ToString() ?? "",
                TargetPortIndex = connector.End?.Index ?? -1
            };
        }

        private List<DisconnectedPortInfo> FindDisconnectedInputs(WorkspaceModel workspace)
        {
            var disconnected = new List<DisconnectedPortInfo>();

            foreach (var node in workspace.Nodes)
            {
                foreach (var port in node.InPorts)
                {
                    // Check if port has no connections and is likely required
                    if (!port.Connectors.Any())
                    {
                        // Heuristic: ports with default values might be optional
                        bool isLikelyRequired = !port.UsingDefaultValue;
                        
                        disconnected.Add(new DisconnectedPortInfo
                        {
                            NodeId = node.GUID.ToString(),
                            NodeName = node.Name ?? node.GetType().Name,
                            PortName = port.Name ?? $"IN[{port.Index}]",
                            PortIndex = port.Index,
                            IsLikelyRequired = isLikelyRequired
                        });
                    }
                }
            }

            return disconnected;
        }

        /// <summary>
        /// Serialize graph data to JSON for AI consumption
        /// </summary>
        public string ToJson(GraphAnalysisData data)
        {
#if NET48
            return JsonConvert.SerializeObject(data, Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
#else
            return JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
#endif
        }
    }

    #region Data Models

    public class GraphAnalysisData
    {
        public string WorkspaceName { get; set; }
        public int NodeCount { get; set; }
        public int ConnectorCount { get; set; }
        public string Error { get; set; }
        
        // Environment info for version-aware analysis
        public EnvironmentInfo Environment { get; set; }
        
        public List<NodeData> Nodes { get; set; } = new List<NodeData>();
        public List<WireData> Wires { get; set; } = new List<WireData>();
        public List<DisconnectedPortInfo> DisconnectedInputs { get; set; } = new List<DisconnectedPortInfo>();
        public List<GroupData> Groups { get; set; } = new List<GroupData>();
    }

    public class EnvironmentInfo
    {
        public string RevitVersion { get; set; }
        public string DynamoVersion { get; set; }
        public string PythonEngine { get; set; }  // IronPython 2.7 or CPython 3.x
    }

    public class NodeData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string OriginalName { get; set; }
        public string Category { get; set; }
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public string State { get; set; }
        public string ErrorMessage { get; set; }
        public string PythonCode { get; set; }
        public string CodeBlockContent { get; set; }  // Code Block 노드 내용
        public string InputValue { get; set; }        // Number/String/Boolean 입력값
        public string LacingMode { get; set; }        // Lacing 설정 (Auto, Shortest, Longest, CrossProduct)
        public List<PortData> InputPorts { get; set; } = new List<PortData>();
        public List<PortData> OutputPorts { get; set; } = new List<PortData>();
    }

    public class PortData
    {
        public string Name { get; set; }
        public int Index { get; set; }
        public bool IsConnected { get; set; }
        public string DataType { get; set; }
        public CachedValueData CachedValue { get; set; }
    }

    public class CachedValueData
    {
        public string Type { get; set; }
        public string Value { get; set; }
    }

    public class WireData
    {
        public string SourceNodeId { get; set; }
        public int SourcePortIndex { get; set; }
        public string TargetNodeId { get; set; }
        public int TargetPortIndex { get; set; }
    }

    public class DisconnectedPortInfo
    {
        public string NodeId { get; set; }
        public string NodeName { get; set; }
        public string PortName { get; set; }
        public int PortIndex { get; set; }
        public bool IsLikelyRequired { get; set; }
    }

    public class GroupData
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public List<string> NodeIds { get; set; } = new List<string>();
    }

    #endregion
}
