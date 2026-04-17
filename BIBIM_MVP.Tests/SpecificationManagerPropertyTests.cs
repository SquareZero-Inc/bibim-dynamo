// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using FsCheck;
using FsCheck.Xunit;
using Xunit;
using BIBIM_MVP;

namespace BIBIM_MVP.Tests
{
    /// <summary>
    /// Property-based tests for SpecificationManager service.
    /// Feature: spec-first-code-generation
    /// </summary>
    public class SpecificationManagerPropertyTests
    {
        /// <summary>
        /// Feature: spec-first-code-generation, Property 12: Pending State Persistence Until Action
        /// 
        /// For any SpecificationManager where SetPendingSpec has been called, HasPendingSpec SHALL 
        /// remain true until either ConfirmPendingSpec or ClearPendingSpec is called.
        /// 
        /// **Validates: Requirements 4.1**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property PendingStatePersistenceUntilAction()
        {
            return Prop.ForAll(
                CreateCodeSpecificationArbitrary(),
                Gen.Choose(0, 10).ToArbitrary(),
                (CodeSpecification spec, int readCount) =>
                {
                    // Arrange: Create a fresh SpecificationManager
                    var manager = new SpecificationManager();

                    // Act: Set a pending spec
                    manager.SetPendingSpec(spec);

                    // Assert: HasPendingSpec should be true immediately after SetPendingSpec
                    var hasPendingAfterSet = manager.HasPendingSpec;

                    // Act: Read the pending spec multiple times (simulating multiple accesses)
                    // This should NOT affect the pending state
                    for (int i = 0; i < readCount; i++)
                    {
                        var _ = manager.GetPendingSpec();
                        var __ = manager.HasPendingSpec;
                    }

                    // Assert: HasPendingSpec should still be true after multiple reads
                    var hasPendingAfterReads = manager.HasPendingSpec;

                    return hasPendingAfterSet && hasPendingAfterReads;
                });
        }

        /// <summary>
        /// Feature: spec-first-code-generation, Property 12: Pending State Persistence Until Action (Confirm Path)
        /// 
        /// For any SpecificationManager where SetPendingSpec has been called, HasPendingSpec SHALL 
        /// remain true until ConfirmPendingSpec is called, after which it SHALL be false.
        /// 
        /// **Validates: Requirements 4.1**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property PendingStatePersistenceUntilConfirm()
        {
            return Prop.ForAll(
                CreateCodeSpecificationArbitrary(),
                spec =>
                {
                    // Arrange: Create a fresh SpecificationManager
                    var manager = new SpecificationManager();

                    // Act: Set a pending spec
                    manager.SetPendingSpec(spec);

                    // Assert: HasPendingSpec should be true before confirm
                    var hasPendingBeforeConfirm = manager.HasPendingSpec;

                    // Act: Confirm the pending spec
                    manager.ConfirmPendingSpec();

                    // Assert: HasPendingSpec should be false after confirm
                    var hasPendingAfterConfirm = manager.HasPendingSpec;

                    return hasPendingBeforeConfirm && !hasPendingAfterConfirm;
                });
        }

        /// <summary>
        /// Feature: spec-first-code-generation, Property 12: Pending State Persistence Until Action (Clear Path)
        /// 
        /// For any SpecificationManager where SetPendingSpec has been called, HasPendingSpec SHALL 
        /// remain true until ClearPendingSpec is called, after which it SHALL be false.
        /// 
        /// **Validates: Requirements 4.1**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property PendingStatePersistenceUntilClear()
        {
            return Prop.ForAll(
                CreateCodeSpecificationArbitrary(),
                spec =>
                {
                    // Arrange: Create a fresh SpecificationManager
                    var manager = new SpecificationManager();

                    // Act: Set a pending spec
                    manager.SetPendingSpec(spec);

                    // Assert: HasPendingSpec should be true before clear
                    var hasPendingBeforeClear = manager.HasPendingSpec;

                    // Act: Clear the pending spec
                    manager.ClearPendingSpec();

                    // Assert: HasPendingSpec should be false after clear
                    var hasPendingAfterClear = manager.HasPendingSpec;

                    return hasPendingBeforeClear && !hasPendingAfterClear;
                });
        }

        /// <summary>
        /// Feature: spec-first-code-generation, Property 12: Pending State Persistence Until Action (Multiple Sets)
        /// 
        /// For any sequence of SetPendingSpec calls, HasPendingSpec SHALL remain true until either 
        /// ConfirmPendingSpec or ClearPendingSpec is called.
        /// 
        /// **Validates: Requirements 4.1**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property PendingStatePersistenceWithMultipleSets()
        {
            return Prop.ForAll(
                CreateCodeSpecificationArbitrary(),
                CreateCodeSpecificationArbitrary(),
                CreateCodeSpecificationArbitrary(),
                (CodeSpecification spec1, CodeSpecification spec2, CodeSpecification spec3) =>
                {
                    // Arrange: Create a fresh SpecificationManager
                    var manager = new SpecificationManager();

                    // Act: Set multiple pending specs in sequence
                    manager.SetPendingSpec(spec1);
                    var hasPendingAfterFirst = manager.HasPendingSpec;

                    manager.SetPendingSpec(spec2);
                    var hasPendingAfterSecond = manager.HasPendingSpec;

                    manager.SetPendingSpec(spec3);
                    var hasPendingAfterThird = manager.HasPendingSpec;

                    // Assert: HasPendingSpec should be true after each set
                    // The last spec should be the current pending spec
                    var currentSpec = manager.GetPendingSpec();
                    var isLastSpecCurrent = currentSpec.SpecId == spec3.SpecId;

                    return hasPendingAfterFirst && hasPendingAfterSecond && hasPendingAfterThird && isLastSpecCurrent;
                });
        }

        /// <summary>
        /// Feature: spec-first-code-generation, Property 12: Pending State Persistence Until Action (No Other Operations Affect State)
        /// 
        /// For any SpecificationManager where SetPendingSpec has been called, only ConfirmPendingSpec 
        /// or ClearPendingSpec should change HasPendingSpec to false. GetPendingSpec should not affect state.
        /// 
        /// **Validates: Requirements 4.1**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property GetPendingSpecDoesNotAffectState()
        {
            return Prop.ForAll(
                CreateCodeSpecificationArbitrary(),
                Gen.Choose(1, 20).ToArbitrary(),
                (CodeSpecification spec, int getCount) =>
                {
                    // Arrange: Create a fresh SpecificationManager
                    var manager = new SpecificationManager();

                    // Act: Set a pending spec
                    manager.SetPendingSpec(spec);

                    // Act: Call GetPendingSpec multiple times
                    for (int i = 0; i < getCount; i++)
                    {
                        var retrievedSpec = manager.GetPendingSpec();
                        // Verify the retrieved spec is the same as what was set
                        if (retrievedSpec == null || retrievedSpec.SpecId != spec.SpecId)
                        {
                            return false;
                        }
                    }

                    // Assert: HasPendingSpec should still be true
                    return manager.HasPendingSpec;
                });
        }

        /// <summary>
        /// Feature: spec-first-code-generation, Property 9: Confirm Clears Pending State
        /// 
        /// For any SpecificationManager with a pending specification, calling ConfirmPendingSpec 
        /// followed by checking HasPendingSpec SHALL return false.
        /// 
        /// **Validates: Requirements 4.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property ConfirmClearsPendingState()
        {
            return Prop.ForAll(
                CreateCodeSpecificationArbitrary(),
                spec =>
                {
                    // Arrange: Create a fresh SpecificationManager with a pending spec
                    var manager = new SpecificationManager();
                    manager.SetPendingSpec(spec);

                    // Verify precondition: HasPendingSpec should be true before confirm
                    var hasPendingBeforeConfirm = manager.HasPendingSpec;

                    // Act: Confirm the pending spec
                    manager.ConfirmPendingSpec();

                    // Assert: HasPendingSpec SHALL return false after ConfirmPendingSpec
                    var hasPendingAfterConfirm = manager.HasPendingSpec;

                    return hasPendingBeforeConfirm && !hasPendingAfterConfirm;
                });
        }

        /// <summary>
        /// Feature: spec-first-code-generation, Property 9: Confirm Clears Pending State (Idempotent)
        /// 
        /// For any SpecificationManager, calling ConfirmPendingSpec multiple times SHALL always 
        /// result in HasPendingSpec being false (idempotent behavior).
        /// 
        /// **Validates: Requirements 4.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property ConfirmClearsPendingStateIdempotent()
        {
            return Prop.ForAll(
                CreateCodeSpecificationArbitrary(),
                Gen.Choose(1, 5).ToArbitrary(),
                (CodeSpecification spec, int confirmCount) =>
                {
                    // Arrange: Create a fresh SpecificationManager with a pending spec
                    var manager = new SpecificationManager();
                    manager.SetPendingSpec(spec);

                    // Act: Call ConfirmPendingSpec multiple times
                    for (int i = 0; i < confirmCount; i++)
                    {
                        manager.ConfirmPendingSpec();
                    }

                    // Assert: HasPendingSpec SHALL return false after any number of confirms
                    return !manager.HasPendingSpec;
                });
        }

        /// <summary>
        /// Feature: spec-first-code-generation, Property 9: Confirm Clears Pending State (Sets IsConfirmed)
        /// 
        /// For any SpecificationManager with a pending specification, calling ConfirmPendingSpec 
        /// SHALL set IsConfirmed to true on the specification before clearing.
        /// 
        /// **Validates: Requirements 4.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property ConfirmSetsIsConfirmedBeforeClearing()
        {
            return Prop.ForAll(
                CreateCodeSpecificationArbitrary(),
                spec =>
                {
                    // Arrange: Create a fresh SpecificationManager
                    var manager = new SpecificationManager();
                    
                    // Ensure spec starts as not confirmed
                    spec.IsConfirmed = false;
                    manager.SetPendingSpec(spec);

                    // Track the IsConfirmed state via event
                    bool? isConfirmedInEvent = null;
                    manager.SpecStateChanged += (sender, e) =>
                    {
                        if (e.ChangeType == SpecStateChangeType.SpecConfirmed)
                        {
                            // The spec should have been marked as confirmed
                            // Note: The spec object itself is modified before clearing
                            isConfirmedInEvent = spec.IsConfirmed;
                        }
                    };

                    // Act: Confirm the pending spec
                    manager.ConfirmPendingSpec();

                    // Assert: The spec's IsConfirmed should be true (modified in place)
                    // and HasPendingSpec should be false
                    return spec.IsConfirmed && !manager.HasPendingSpec;
                });
        }

        /// <summary>
        /// Feature: spec-first-code-generation, Property 9: Confirm Clears Pending State (No Pending Spec)
        /// 
        /// For any SpecificationManager without a pending specification, calling ConfirmPendingSpec 
        /// SHALL be a safe no-op and HasPendingSpec SHALL remain false.
        /// 
        /// **Validates: Requirements 4.2**
        /// </summary>
        [Fact]
        public void ConfirmWithNoPendingSpecIsNoOp()
        {
            // Arrange: Create a fresh SpecificationManager with no pending spec
            var manager = new SpecificationManager();

            // Verify precondition: HasPendingSpec should be false initially
            Assert.False(manager.HasPendingSpec);

            // Act: Confirm when there's no pending spec (should be safe no-op)
            manager.ConfirmPendingSpec();

            // Assert: HasPendingSpec should still be false
            Assert.False(manager.HasPendingSpec);
        }

        /// <summary>
        /// Creates a custom Arbitrary for CodeSpecification that generates valid specifications.
        /// </summary>
        private static Arbitrary<CodeSpecification> CreateCodeSpecificationArbitrary()
        {
            var specGen = from specId in Arb.Generate<Guid>()
                          from originalRequest in Arb.Generate<NonEmptyString>()
                          from inputCount in Gen.Choose(0, 5)
                          from stepCount in Gen.Choose(1, 10)
                          from questionCount in Gen.Choose(0, 3)
                          from revisionNumber in Gen.Choose(1, 10)
                          from isConfirmed in Arb.Generate<bool>()
                          select CreateCodeSpecificationInternal(
                              specId.ToString(),
                              originalRequest.Get,
                              inputCount,
                              stepCount,
                              questionCount,
                              revisionNumber,
                              isConfirmed);

            return specGen.ToArbitrary();
        }

        /// <summary>
        /// Internal helper to create a fully configured CodeSpecification.
        /// </summary>
        private static CodeSpecification CreateCodeSpecificationInternal(
            string specId,
            string originalRequest,
            int inputCount,
            int stepCount,
            int questionCount,
            int revisionNumber,
            bool isConfirmed)
        {
            var spec = new CodeSpecification
            {
                SpecId = specId,
                OriginalRequest = originalRequest,
                RevisionNumber = revisionNumber,
                IsConfirmed = isConfirmed,
                CreatedAt = DateTime.UtcNow
            };

            // Populate inputs
            spec.Inputs = new List<SpecInput>();
            for (int i = 0; i < inputCount; i++)
            {
                spec.Inputs.Add(new SpecInput
                {
                    Name = $"Input {i + 1}",
                    Type = "Element",
                    Description = $"Description for input {i + 1}"
                });
            }

            // Populate processing steps
            spec.ProcessingSteps = new List<string>();
            for (int i = 0; i < stepCount; i++)
            {
                spec.ProcessingSteps.Add($"Step {i + 1}: Process data");
            }

            // Populate output
            spec.Output = new SpecOutput
            {
                Type = "Result",
                Description = "Output description",
                Unit = "units"
            };

            // Populate clarifying questions
            spec.ClarifyingQuestions = new List<string>();
            for (int i = 0; i < questionCount; i++)
            {
                spec.ClarifyingQuestions.Add($"Question {i + 1}?");
            }

            return spec;
        }
    }
}
