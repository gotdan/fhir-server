﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.AccessTokenProvider
{
    public class AccessTokenProviderFactory : IAccessTokenProviderFactory
    {
        private Dictionary<string, Func<IAccessTokenProvider>> _registeredTypes;

        public AccessTokenProviderFactory(IEnumerable<Func<IAccessTokenProvider>> accessTokenProviderFactories)
        {
            EnsureArg.IsNotNull(accessTokenProviderFactories, nameof(accessTokenProviderFactories));

            _registeredTypes = accessTokenProviderFactories.ToDictionary(factory => factory().AccessTokenProviderType, StringComparer.Ordinal);
        }

        /// <inheritdoc />
        public IAccessTokenProvider Create(string destinationType)
        {
            EnsureArg.IsNotNullOrWhiteSpace(destinationType, nameof(destinationType));

            if (_registeredTypes.TryGetValue(destinationType, out Func<IAccessTokenProvider> factory))
            {
                return factory();
            }

            throw new UnsupportedAccessTokenProviderException(destinationType);
        }

        public bool IsSupportedAccessTokenProviderType(string destinationType)
        {
            return _registeredTypes.ContainsKey(destinationType);
        }
    }
}