﻿#region Copyright notice and license

// Copyright 2019 The gRPC Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#endregion

using System;
using System.Reflection;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.AspNetCore.Server.Internal;
using Microsoft.AspNetCore.Http;

namespace Grpc.AspNetCore.Server.Internal
{
    internal abstract class ServerCallHandlerBase<TRequest, TResponse, TService> : IServerCallHandler
    {
        private readonly object _lock;
        protected Method<TRequest, TResponse> Method { get; }

        private ObjectMethodExecutor _objectMethodExecutor;

        protected ServerCallHandlerBase(Method<TRequest, TResponse> method)
        {
            _lock = new object();
            Method = method ?? throw new ArgumentNullException(nameof(method));
        }

        public abstract Task HandleCallAsync(HttpContext httpContext);

        protected ObjectMethodExecutor GetMethodExecutor()
        {
            if (_objectMethodExecutor == null)
            {
                lock (_lock)
                {
                    if (_objectMethodExecutor == null)
                    {
                        var handlerMethod = typeof(TService).GetMethod(Method.Name);

                        _objectMethodExecutor = ObjectMethodExecutor.Create(
                            handlerMethod,
                            typeof(TService).GetTypeInfo());
                    }
                }
            }

            return _objectMethodExecutor;
        }
    }
}
