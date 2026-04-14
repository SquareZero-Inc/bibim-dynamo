using System;

namespace BIBIM_MVP
{
    /// <summary>
    /// Event arguments for specification state changes.
    /// </summary>
    public class SpecStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Indicates whether there is a pending specification after the state change.
        /// </summary>
        public bool HasPendingSpec { get; }

        /// <summary>
        /// The current pending specification, or null if none exists.
        /// </summary>
        public CodeSpecification PendingSpec { get; }

        /// <summary>
        /// The type of state change that occurred.
        /// </summary>
        public SpecStateChangeType ChangeType { get; }

        public SpecStateChangedEventArgs(bool hasPendingSpec, CodeSpecification pendingSpec, SpecStateChangeType changeType)
        {
            HasPendingSpec = hasPendingSpec;
            PendingSpec = pendingSpec;
            ChangeType = changeType;
        }
    }

    /// <summary>
    /// Types of specification state changes.
    /// </summary>
    public enum SpecStateChangeType
    {
        /// <summary>A new specification was set as pending.</summary>
        SpecSet,
        /// <summary>The pending specification was confirmed.</summary>
        SpecConfirmed,
        /// <summary>The pending specification was cleared/cancelled.</summary>
        SpecCleared
    }

    /// <summary>
    /// Manages pending specification state and lifecycle.
    /// Provides thread-safe state management for the spec-first code generation flow.
    /// </summary>
    /// <remarks>
    /// Requirements:
    /// - 4.1: THE Confirmation_Handler SHALL maintain a Pending_Spec state that persists until confirmation or cancellation
    /// - 4.2: WHEN a Pending_Spec is confirmed, THE system SHALL clear the pending state and proceed to code generation
    /// 
    /// Correctness Properties:
    /// - Property 9: Confirm Clears Pending State - calling ConfirmPendingSpec followed by checking HasPendingSpec SHALL return false
    /// - Property 12: Pending State Persistence Until Action - HasPendingSpec SHALL remain true until either ConfirmPendingSpec or ClearPendingSpec is called
    /// </remarks>
    public class SpecificationManager
    {
        private CodeSpecification _pendingSpec;
        private readonly object _lock = new object();

        /// <summary>
        /// Event raised when pending spec state changes.
        /// </summary>
        public event EventHandler<SpecStateChangedEventArgs> SpecStateChanged;

        /// <summary>
        /// Returns true if there is a pending specification awaiting confirmation.
        /// Thread-safe property access.
        /// </summary>
        public bool HasPendingSpec
        {
            get
            {
                lock (_lock)
                {
                    return _pendingSpec != null;
                }
            }
        }

        /// <summary>
        /// Gets the current pending specification.
        /// </summary>
        /// <returns>The pending specification, or null if none exists.</returns>
        public CodeSpecification GetPendingSpec()
        {
            lock (_lock)
            {
                return _pendingSpec;
            }
        }

        /// <summary>
        /// Sets a new pending specification.
        /// </summary>
        /// <param name="spec">The specification to set as pending.</param>
        /// <exception cref="ArgumentNullException">Thrown when spec is null.</exception>
        public void SetPendingSpec(CodeSpecification spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            lock (_lock)
            {
                _pendingSpec = spec;
            }

            Log($"SetPendingSpec: SpecId={spec.SpecId}, Revision={spec.RevisionNumber}");
            OnSpecStateChanged(new SpecStateChangedEventArgs(true, spec, SpecStateChangeType.SpecSet));
        }

        /// <summary>
        /// Clears the pending specification after confirmation or cancellation.
        /// Safe to call even if no pending spec exists.
        /// </summary>
        public void ClearPendingSpec()
        {
            lock (_lock)
            {
                _pendingSpec = null;
            }

            Log("ClearPendingSpec: Pending spec cleared");
            OnSpecStateChanged(new SpecStateChangedEventArgs(false, null, SpecStateChangeType.SpecCleared));
        }

        /// <summary>
        /// Marks the pending spec as confirmed and clears the pending state.
        /// This method sets IsConfirmed to true on the spec before clearing.
        /// </summary>
        /// <remarks>
        /// Property 9: After calling this method, HasPendingSpec will return false.
        /// </remarks>
        public void ConfirmPendingSpec()
        {
            CodeSpecification confirmedSpec = null;

            lock (_lock)
            {
                if (_pendingSpec != null)
                {
                    _pendingSpec.IsConfirmed = true;
                    confirmedSpec = _pendingSpec;
                    _pendingSpec = null;
                }
            }

            if (confirmedSpec != null)
            {
                Log($"ConfirmPendingSpec: SpecId={confirmedSpec.SpecId} confirmed and cleared");
                OnSpecStateChanged(new SpecStateChangedEventArgs(false, null, SpecStateChangeType.SpecConfirmed));
            }
            else
            {
                Log("ConfirmPendingSpec: No pending spec to confirm (no-op)");
            }
        }

        /// <summary>
        /// Raises the SpecStateChanged event.
        /// </summary>
        /// <param name="e">Event arguments containing state change information.</param>
        protected virtual void OnSpecStateChanged(SpecStateChangedEventArgs e)
        {
            SpecStateChanged?.Invoke(this, e);
        }

        private void Log(string message)
        {
            Logger.Log("SpecificationManager", message);
        }
    }
}
