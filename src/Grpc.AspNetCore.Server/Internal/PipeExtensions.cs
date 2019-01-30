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
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace Grpc.AspNetCore.Server.Internal
{
    internal static class PipeExtensions
    {
        private const int MessageDelimiterSize = 4; // how many bytes it takes to encode "Message-Length"
        private const int HeaderSize = MessageDelimiterSize + 1; // message length + compression flag

        public static Task WriteMessageAsync(this PipeWriter pipeWriter, byte[] messageData, bool flush = false)
        {
            WriteHeader(pipeWriter, messageData.Length);
            pipeWriter.Write(messageData);

            if (flush)
            {
                return FlushWriterAsync(pipeWriter);
            }

            return Task.CompletedTask;
        }

        private static async Task FlushWriterAsync(PipeWriter pipeWriter)
        {
            await pipeWriter.FlushAsync();
        }

        private static void WriteHeader(PipeWriter pipeWriter, int length)
        {
            Span<byte> headerData = pipeWriter.GetSpan(HeaderSize);
            headerData[0] = 0;
            EncodeMessageLength(length, headerData.Slice(1));

            pipeWriter.Advance(HeaderSize);
        }

        private static void EncodeMessageLength(int messageLength, Span<byte> destination)
        {
            if (destination.Length < MessageDelimiterSize)
            {
                throw new ArgumentException("Buffer too small to encode message length.");
            }

            BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)messageLength);
        }

        private static int DecodeMessageLength(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < MessageDelimiterSize)
            {
                throw new ArgumentException("Buffer too small to decode message length.");
            }

            var result = BinaryPrimitives.ReadUInt32BigEndian(buffer);

            if (result > int.MaxValue)
            {
                throw new IOException("Message too large: " + result);
            }

            return (int)result;
        }

        private static bool TryReadHeader(ReadOnlySequence<byte> buffer, out bool compressed, out int messageLength)
        {
            if (buffer.Length < HeaderSize)
            {
                compressed = false;
                messageLength = 0;
                return false;
            }

            if (buffer.First.Length >= HeaderSize)
            {
                var headerData = buffer.First.Span.Slice(0, HeaderSize);

                compressed = ReadCompressedFlag(headerData[0]);
                messageLength = DecodeMessageLength(headerData.Slice(1));
            }
            else
            {
                Span<byte> headerData = stackalloc byte[HeaderSize];
                buffer.Slice(0, HeaderSize).CopyTo(headerData);

                compressed = ReadCompressedFlag(headerData[0]);
                messageLength = DecodeMessageLength(headerData.Slice(1));
            }

            return true;
        }

        private static bool ReadCompressedFlag(byte flag)
        {
            if (flag == 0)
            {
                return false;
            }
            else if (flag == 1)
            {
                return true;
            }
            else
            {
                throw new InvalidOperationException("Unexpected compressed flag value in message header.");
            }
        }

        public static ValueTask<byte[]> ReadMessageAsync(this PipeReader pipeReader)
        {
            var resultTask = pipeReader.ReadAsync();

            // Avoid state machine when sync
            if (resultTask.IsCompletedSuccessfully)
            {
                var result = resultTask.Result;

                var message = ReadMessage(result);

                // Move pipe to the end of the read message
                pipeReader.AdvanceTo(result.Buffer.End);

                return new ValueTask<byte[]>(message);
            }
            else
            {
                return ReadMessageSlowAsync(resultTask, pipeReader);
            }
        }

        private static async ValueTask<byte[]> ReadMessageSlowAsync(ValueTask<ReadResult> task, PipeReader pipeReader)
        {
            var result = await task;

            var message = ReadMessage(result);

            // Move pipe to the end of the read message
            pipeReader.AdvanceTo(result.Buffer.End);

            return message;
        }

        private static byte[] ReadMessage(ReadResult result)
        {
            if (result.IsCompleted)
            {
                return null;
            }

            var buffer = result.Buffer;

            if (!TryReadHeader(buffer, out var compressed, out var messageLength))
            {
                throw new InvalidOperationException("Unable to read the message header.");
            }

            if (compressed)
            {
                // TODO(jtattermusch): support compressed messages
                throw new IOException("Compressed messages are not yet supported.");
            }

            if (buffer.Length < HeaderSize + messageLength)
            {
                throw new InvalidOperationException($"Unable to read complete message data. Expected {messageLength} bytes.");
            }

            var messageBuffer = buffer.Slice(HeaderSize, messageLength);

            var messageData = messageBuffer.ToArray();

            return messageData;
        }
    }
}
