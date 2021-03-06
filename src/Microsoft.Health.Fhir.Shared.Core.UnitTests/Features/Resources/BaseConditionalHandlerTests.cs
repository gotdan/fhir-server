﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Resources;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;
using static Hl7.Fhir.Model.Bundle;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources
{
    public class BaseConditionalHandlerTests
    {
        private readonly TestBaseConditionalHandler _testBaseConditionalHandler;
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();

        public BaseConditionalHandlerTests()
        {
            IFhirDataStore fhirDataStore = Substitute.For<IFhirDataStore>();
            Lazy<IConformanceProvider> conformanceProvider = Substitute.For<Lazy<IConformanceProvider>>();
            IResourceWrapperFactory resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
            ResourceIdProvider resourceIdProvider = Substitute.For<ResourceIdProvider>();
            _testBaseConditionalHandler = new TestBaseConditionalHandler(fhirDataStore, _searchService, conformanceProvider, resourceWrapperFactory, resourceIdProvider);
        }

        [Fact]
        public async Task GivenATransactionBundleWithIdentifierReferences_WhenResolved_ThenReferencesValuesAreNotUpdated()
        {
            var observation = new Observation
            {
                Subject = new ResourceReference
                {
                    Identifier = new Identifier("https://example.com", "12345"),
                },
            };

            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Resource = observation,
                    },
                },
            };

            var referenceIdDictionary = new Dictionary<string, (string resourceId, string resourceType)>();

            foreach (var entry in bundle.Entry)
            {
                var references = entry.Resource.GetAllChildren<ResourceReference>().ToList();

                // Asserting the conditional reference value before resolution
                Assert.Null(references.First().Reference);
                var requestUrl = (entry.Request != null) ? entry.Request.Url : null;
                await _testBaseConditionalHandler.ResolveReferencesAsync(entry.Resource, referenceIdDictionary, requestUrl, CancellationToken.None);

                // Asserting the resolved reference value after resolution
                Assert.Null(references.First().Reference);
            }
        }

        [Fact]
        public async Task GivenATransactionBundleWithConditionalReferences_WhenResolved_ThenReferencesValuesAreUpdatedCorrectly()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithConditionalReferenceInResourceBody");
            var bundle = requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>();

            SearchResultEntry mockSearchEntry = GetMockSearchEntry("123", KnownResourceTypes.Patient);

            var searchResult = new SearchResult(new[] { mockSearchEntry }, new Tuple<string, string>[0], Array.Empty<(string parameterName, string reason)>(), null);
            _searchService.SearchAsync("Patient", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None).Returns(searchResult);

            var referenceIdDictionary = new Dictionary<string, (string resourceId, string resourceType)>();

            foreach (var entry in bundle.Entry)
            {
                var references = entry.Resource.GetAllChildren<ResourceReference>().ToList();

                // Asserting the conditional reference value before resolution
                Assert.Equal("Patient?identifier=12345", references.First().Reference);

                var requestUrl = (entry.Request != null) ? entry.Request.Url : null;
                await _testBaseConditionalHandler.ResolveReferencesAsync(entry.Resource, referenceIdDictionary, requestUrl, CancellationToken.None);

                // Asserting the resolved reference value after resolution
                Assert.Equal("Patient/123", references.First().Reference);
            }
        }

        [Fact]
        public async Task GivenATransactionBundleWithConditionalReferences_WhenNotResolved_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithConditionalReferenceInResourceBody");
            var bundle = requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>();

            SearchResultEntry mockSearchEntry = GetMockSearchEntry("123", KnownResourceTypes.Patient);
            SearchResultEntry mockSearchEntry1 = GetMockSearchEntry("123", KnownResourceTypes.Patient);

            var expectedMessage = "Given conditional reference 'Patient?identifier=12345' does not resolve to a resource.";

            var searchResult = new SearchResult(new[] { mockSearchEntry, mockSearchEntry1 }, new Tuple<string, string>[0], Array.Empty<(string parameterName, string reason)>(), null);
            _searchService.SearchAsync("Patient", Arg.Any<IReadOnlyList<Tuple<string, string>>>(), CancellationToken.None).Returns(searchResult);

            var referenceIdDictionary = new Dictionary<string, (string resourceId, string resourceType)>();
            foreach (var entry in bundle.Entry)
            {
                var requestUrl = (entry.Request != null) ? entry.Request.Url : null;
                var exception = await Assert.ThrowsAsync<RequestNotValidException>(() => _testBaseConditionalHandler.ResolveReferencesAsync(entry.Resource, referenceIdDictionary, requestUrl, CancellationToken.None));
                Assert.Equal(exception.Message, expectedMessage);
            }
        }

        [Fact]
        public async Task GivenATransactionBundleWithInvalidResourceTypeInReference_WhenExecuted_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithInvalidResourceType");
            var bundle = requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>();

            var expectedMessage = "Resource type 'Patientt' in the reference 'Patientt?identifier=12345' is not supported.";

            var referenceIdDictionary = new Dictionary<string, (string resourceId, string resourceType)>();
            foreach (var entry in bundle.Entry)
            {
                var requestUrl = (entry.Request != null) ? entry.Request.Url : null;
                var exception = await Assert.ThrowsAsync<RequestNotValidException>(() => _testBaseConditionalHandler.ResolveReferencesAsync(entry.Resource, referenceIdDictionary, requestUrl, CancellationToken.None));
                Assert.Equal(exception.Message, expectedMessage);
            }
        }

        private static SearchResultEntry GetMockSearchEntry(string resourceId, string resourceType)
        {
            return new SearchResultEntry(
               new ResourceWrapper(
                   resourceId,
                   "1",
                   resourceType,
                   new RawResource("data", FhirResourceFormat.Json),
                   null,
                   DateTimeOffset.MinValue,
                   false,
                   null,
                   null,
                   null));
        }
    }
}
