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
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace Grpc.AspNetCore.Server.Internal
{
    internal static class PipeUtils
    {
        private const int MessageDelimiterSize = 4; // how many bytes it takes to encode "Message-Length"
        private const int HeaderSize = MessageDelimiterSize + 1; // message length + compression flag

        public static async Task WriteMessageAsync(PipeWriter bufferWriter, byte[] messageData, bool flush = false)
        {
            WriteHeader(bufferWriter, messageData.Length);
            bufferWriter.Write(messageData);

            if (flush)
            {
                await bufferWriter.FlushAsync();
            }
        }

        public static void WriteHeader(IBufferWriter<byte> bufferWriter, int length)
        {
            Span<byte> headerData = stackalloc byte[HeaderSize];
            headerData[0] = 0;
            EncodeMessageLength(length, headerData.Slice(1));

            bufferWriter.Write(headerData);
        }

        public static void EncodeMessageLength(int messageLength, Span<byte> destination)
        {
            if (destination.Length < MessageDelimiterSize)
            {
                throw new ArgumentException("Buffer too small to encode message length.");
            }

            var unsignedValue = (ulong)messageLength;
            for (var i = MessageDelimiterSize - 1; i >= 0; i--)
            {
                // msg length stored in big endian
                destination[i] = (byte)(unsignedValue & 0xff);
                unsignedValue >>= 8;
            }
        }

        private static int DecodeMessageLength(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < MessageDelimiterSize)
            {
                throw new ArgumentException("Buffer too small to decode message length.");
            }

            ulong result = 0;
            for (int i = 0; i < MessageDelimiterSize; i++)
            {
                // msg length stored in big endian
                result = (result << 8) + buffer[i];
            }

            if (result > int.MaxValue)
            {
                throw new IOException("Message too large: " + result);
            }

            return (int)result;
        }

        public static bool TryReadHeader(ReadOnlySequence<byte> buffer, out int messageLength)
        {
            if (buffer.Length < HeaderSize)
            {
                messageLength = 0;
                return false;
            }

            Span<byte> headerData = stackalloc byte[HeaderSize];
            buffer.Slice(0, HeaderSize).CopyTo(headerData);

            var compressionFlag = headerData[0];
            if (compressionFlag != 0)
            {
                // TODO(jtattermusch): support compressed messages
                throw new IOException("Compressed messages are not yet supported.");
            }

            messageLength = DecodeMessageLength(headerData.Slice(1, 4));
            return true;
        }

        public static async ValueTask<byte[]> ReadMessageAsync(PipeReader pipeReader)
        {
            var result = await pipeReader.ReadAsync();

            if (result.IsCompleted)
            {
                return null;
            }

            var buffer = result.Buffer;

            if (!TryReadHeader(buffer, out var messageLength))
            {
                throw new InvalidOperationException("Unable to read the message header.");
            }

            if (buffer.Length < HeaderSize + messageLength)
            {
                throw new InvalidOperationException($"Unable to read complete message data. Expected {messageLength} bytes.");
            }

            var messageBuffer = buffer.Slice(HeaderSize, messageLength);

            var messageData = messageBuffer.ToArray();

            // Move pipe to the end of the read message
            pipeReader.AdvanceTo(buffer.End);

            return messageData;
        }
    }
}
