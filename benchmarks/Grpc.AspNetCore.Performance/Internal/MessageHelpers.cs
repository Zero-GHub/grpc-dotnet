#region Copyright notice and license

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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Grpc.AspNetCore.Performance.Internal
{
    internal static class MessageHelpers
    {
        public const int MessageDelimiterSize = 4;

        public static void WriteMessage(Stream stream, byte[] buffer, int offset, int count)
        {
            var delimiterBuffer = new byte[1 + MessageDelimiterSize];
            delimiterBuffer[0] = 0; // = non-compressed
            EncodeMessageLength(count, new Span<byte>(delimiterBuffer, 1, MessageDelimiterSize));
            stream.Write(delimiterBuffer, 0, delimiterBuffer.Length);

            stream.Write(buffer, offset, count);
        }

        public static void EncodeMessageLength(int messageLength, Span<byte> destination)
        {
            if (destination.Length < MessageDelimiterSize)
            {
                throw new ArgumentException("Buffer too small to encode message length.");
            }

            BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)messageLength);
        }
    }
}
