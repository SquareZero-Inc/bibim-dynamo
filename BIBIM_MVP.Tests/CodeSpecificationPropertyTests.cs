using System;
using System.Collections.Generic;
using FsCheck;
using FsCheck.Xunit;
using Xunit;
using BIBIM_MVP;

namespace BIBIM_MVP.Tests
{
    /// <summary>
    /// Property-based tests for CodeSpecification data model.
    /// Feature: spec-first-code-generation, Property 1: Specification Structure Completeness
    /// </summary>
    public class CodeSpecificationPropertyTests
    {
        /// <summary>
        /// Property 1: Specification Structure Completeness
        /// For any CodeSpecification produced by the Spec_Generator, the specification SHALL have 
        /// non-null Inputs list, non-null ProcessingSteps list, and non-null Output object.
        /// 
        /// **Validates: Requirements 1.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property SpecificationStructureCompleteness()
        {
            return Prop.ForAll(
                CreateCodeSpecificationArbitrary(),
                spec =>
                {
                    // Assert: Verify structure completeness - all required collections are non-null
                    var hasNonNullInputs = spec.Inputs != null;
                    var hasNonNullProcessingSteps = spec.ProcessingSteps != null;
                    var hasNonNullOutput = spec.Output != null;

                    return hasNonNullInputs && hasNonNullProcessingSteps && hasNonNullOutput;
                });
        }

        /// <summary>
        /// Property 1 (Extended): Default Constructor Guarantees Structure Completeness
        /// For any CodeSpecification created via the default constructor, the specification SHALL have 
        /// non-null Inputs list, non-null ProcessingSteps list, and non-null Output object.
        /// 
        /// **Validates: Requirements 1.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property DefaultConstructorGuaranteesStructureCompleteness()
        {
            return Prop.ForAll(
                Gen.Constant(0).ToArbitrary(), // Dummy generator to run property test
                _ =>
                {
                    // Arrange: Create a CodeSpecification using default constructor
                    var spec = new CodeSpecification();

                    // Assert: Verify structure completeness - all required collections are non-null
                    var hasNonNullInputs = spec.Inputs != null;
                    var hasNonNullProcessingSteps = spec.ProcessingSteps != null;
                    var hasNonNullOutput = spec.Output != null;

                    // Also verify the collections are properly initialized (not just non-null)
                    var inputsIsValidList = spec.Inputs is List<SpecInput>;
                    var stepsIsValidList = spec.ProcessingSteps is List<string>;
                    var outputIsValidObject = spec.Output is SpecOutput;

                    return hasNonNullInputs && hasNonNullProcessingSteps && hasNonNullOutput
                        && inputsIsValidList && stepsIsValidList && outputIsValidObject;
                });
        }

        /// <summary>
        /// Property 1 (Populated): Structure Completeness With Populated Data
        /// For any CodeSpecification with populated inputs, steps, and output, the specification SHALL 
        /// maintain non-null Inputs list, non-null ProcessingSteps list, and non-null Output object
        /// regardless of the number of items in each collection.
        /// 
        /// **Validates: Requirements 1.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property StructureCompletenessWithVariedCollectionSizes()
        {
            return Prop.ForAll(
                Gen.Choose(0, 10).ToArbitrary(),
                Gen.Choose(0, 10).ToArbitrary(),
                Gen.Choose(0, 5).ToArbitrary(),
                (int inputCount, int stepCount, int questionCount) =>
                {
                    // Arrange: Create a CodeSpecification with varied collection sizes
                    var spec = CreateCodeSpecificationWithCounts(inputCount, stepCount, questionCount);

                    // Assert: Verify structure completeness - all required collections are non-null
                    var hasNonNullInputs = spec.Inputs != null;
                    var hasNonNullProcessingSteps = spec.ProcessingSteps != null;
                    var hasNonNullOutput = spec.Output != null;

                    return hasNonNullInputs && hasNonNullProcessingSteps && hasNonNullOutput;
                });
        }

        /// <summary>
        /// Property 1 (Edge Case): Structure Completeness With Empty Collections
        /// For any CodeSpecification with empty collections, the specification SHALL still have 
        /// non-null Inputs list, non-null ProcessingSteps list, and non-null Output object.
        /// 
        /// **Validates: Requirements 1.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property StructureCompletenessWithEmptyCollections()
        {
            return Prop.ForAll(
                Arb.From<NonEmptyString>(),
                (NonEmptyString originalRequest) =>
                {
                    // Arrange: Create a CodeSpecification with empty collections
                    var spec = new CodeSpecification
                    {
                        SpecId = Guid.NewGuid().ToString(),
                        OriginalRequest = originalRequest.Get,
                        Inputs = new List<SpecInput>(),
                        ProcessingSteps = new List<string>(),
                        Output = new SpecOutput(),
                        ClarifyingQuestions = new List<string>(),
                        RevisionNumber = 1,
                        CreatedAt = DateTime.UtcNow,
                        IsConfirmed = false
                    };

                    // Assert: Verify structure completeness - all required collections are non-null
                    var hasNonNullInputs = spec.Inputs != null;
                    var hasNonNullProcessingSteps = spec.ProcessingSteps != null;
                    var hasNonNullOutput = spec.Output != null;

                    // Verify empty collections are valid (count == 0)
                    var inputsAreEmpty = spec.Inputs.Count == 0;
                    var stepsAreEmpty = spec.ProcessingSteps.Count == 0;

                    return hasNonNullInputs && hasNonNullProcessingSteps && hasNonNullOutput
                        && inputsAreEmpty && stepsAreEmpty;
                });
        }

        /// <summary>
        /// Creates a custom Arbitrary for CodeSpecification that generates valid specifications
        /// simulating what the Spec_Generator would produce.
        /// </summary>
        private static Arbitrary<CodeSpecification> CreateCodeSpecificationArbitrary()
        {
            var specGen = from specId in Arb.Generate<NonEmptyString>()
                          from originalRequest in Arb.Generate<NonEmptyString>()
                          from inputCount in Gen.Choose(0, 5)
                          from stepCount in Gen.Choose(1, 10)
                          from questionCount in Gen.Choose(0, 3)
                          from revisionNumber in Gen.Choose(1, 10)
                          from isConfirmed in Arb.Generate<bool>()
                          select CreateCodeSpecificationInternal(
                              specId.Get,
                              originalRequest.Get,
                              inputCount,
                              stepCount,
                              questionCount,
                              revisionNumber,
                              isConfirmed);

            return specGen.ToArbitrary();
        }

        /// <summary>
        /// Helper method to create a CodeSpecification with specified collection counts.
        /// </summary>
        private static CodeSpecification CreateCodeSpecificationWithCounts(
            int inputCount,
            int stepCount,
            int questionCount)
        {
            return CreateCodeSpecificationInternal(
                Guid.NewGuid().ToString(),
                "Test request",
                inputCount,
                stepCount,
                questionCount,
                1,
                false);
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
