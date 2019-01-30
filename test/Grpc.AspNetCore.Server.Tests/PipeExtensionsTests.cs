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
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Internal;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class PipeExtensionsTests
    {
        [Test]
        public async Task ReadMessageAsync_EmptyMessage_ReturnNoData()
        {
            // Arrange
            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x00 // length = 0
                });

            var pipeReader = new StreamPipeReader(ms);

            // Act
            var messageData = await pipeReader.ReadMessageAsync(supportMultipleMessages: false);

            // Assert
            Assert.AreEqual(0, messageData.Length);
        }

        [Test]
        public async Task ReadMessageAsync_OneByteMessage_ReturnData()
        {
            // Arrange
            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x01, // length = 1
                    0x10
                });

            var pipeReader = new StreamPipeReader(ms);

            // Act
            var messageData = await pipeReader.ReadMessageAsync(supportMultipleMessages: false);

            // Assert
            Assert.AreEqual(1, messageData.Length);
            Assert.AreEqual(0x10, messageData[0]);
        }

        [Test]
        public async Task ReadMessageAsync_LongMessage_ReturnData()
        {
            // Arrange
            var content = Encoding.UTF8.GetBytes("Lorem ipsum dolor sit amet, consectetur adipiscing elit. Nullam varius nibh a blandit mollis. "
                + "In hac habitasse platea dictumst. Proin non quam nec neque convallis commodo. Orci varius natoque penatibus et magnis dis "
                + "parturient montes, nascetur ridiculus mus. Mauris commodo est vehicula, semper arcu eu, ornare urna. Mauris malesuada nisl "
                + "nisl, vitae tincidunt purus vestibulum sit amet. Interdum et malesuada fames ac ante ipsum primis in faucibus.");

            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x01,
                    0xC1 // length = 449
                }.Concat(content).ToArray());

            var pipeReader = new StreamPipeReader(ms);

            // Act
            var messageData = await pipeReader.ReadMessageAsync(supportMultipleMessages: false);

            // Assert
            Assert.AreEqual(449, messageData.Length);
            CollectionAssert.AreEqual(content, messageData);
        }

        [Test]
        public void ReadMessageAsync_HeaderIncomplete_ThrowError()
        {
            // Arrange
            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00
                });

            var pipeReader = new StreamPipeReader(ms);

            // Act
            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                () => pipeReader.ReadMessageAsync(supportMultipleMessages: false).AsTask());

            // Assert
            Assert.AreEqual("Incomplete message.", ex.Message);
        }

        [Test]
        public void ReadMessageAsync_MessageDataIncomplete_ThrowError()
        {
            // Arrange
            var ms = new MemoryStream(new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x02, // length = 2
                    0x10
                });

            var pipeReader = new StreamPipeReader(ms);

            // Act
            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                () => pipeReader.ReadMessageAsync(supportMultipleMessages: false).AsTask());

            // Assert
            Assert.AreEqual("Incomplete message.", ex.Message);
        }

        [Test]
        public async Task WriteMessageAsync_NoFlush_WriteNoData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = new StreamPipeWriter(ms);

            // Act
            await pipeWriter.WriteMessageAsync(Array.Empty<byte>());

            // Assert
            var messageData = ms.ToArray();
            Assert.AreEqual(0, messageData.Length);
        }

        [Test]
        public async Task WriteMessageAsync_EmptyMessage_WriteMessageWithNoData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = new StreamPipeWriter(ms);

            // Act
            await pipeWriter.WriteMessageAsync(Array.Empty<byte>(), flush: true);

            // Assert
            var messageData = ms.ToArray();

            CollectionAssert.AreEqual(
                new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x00, // length = 0
                },
                messageData);
        }

        [Test]
        public async Task WriteMessageAsync_OneByteMessage_WriteData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = new StreamPipeWriter(ms);

            // Act
            await pipeWriter.WriteMessageAsync(new byte[] { 0x10 }, flush: true);

            // Assert
            var messageData = ms.ToArray();

            CollectionAssert.AreEqual(
                new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x00,
                    0x01, // length = 1
                    0x10
                },
                messageData);
        }

        [Test]
        public async Task WriteMessageAsync_LongMessage_WriteData()
        {
            // Arrange
            var ms = new MemoryStream();
            var pipeWriter = new StreamPipeWriter(ms);
            var content = Encoding.UTF8.GetBytes("Lorem ipsum dolor sit amet, consectetur adipiscing elit. Nullam varius nibh a blandit mollis. "
                + "In hac habitasse platea dictumst. Proin non quam nec neque convallis commodo. Orci varius natoque penatibus et magnis dis "
                + "parturient montes, nascetur ridiculus mus. Mauris commodo est vehicula, semper arcu eu, ornare urna. Mauris malesuada nisl "
                + "nisl, vitae tincidunt purus vestibulum sit amet. Interdum et malesuada fames ac ante ipsum primis in faucibus.");

            // Act
            await pipeWriter.WriteMessageAsync(content, flush: true);

            // Assert
            var messageData = ms.ToArray();

            CollectionAssert.AreEqual(
                new byte[]
                {
                    0x00, // compression = 0
                    0x00,
                    0x00,
                    0x01,
                    0xC1, // length = 449
                }.Concat(content).ToArray(),
                messageData);
        }
    }
}
