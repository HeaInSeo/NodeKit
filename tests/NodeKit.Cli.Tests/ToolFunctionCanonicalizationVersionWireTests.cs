using System;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Nodevault.V1;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// S3-01-A: 벤더 proto(NodeVault b640f96)의 canonicalization_version version delta가
    /// 생성 코드에서 접근 가능하고 고정된 필드 번호로 직렬화되는지 확인한다
    /// (RegisterToolFunctionRequest = 8, RegisterToolFunctionResponse = 4).
    /// 기본값(빈 문자열)은 직렬화되지 않으므로 값을 채우지 않은 기존 메시지의 wire byte는 바뀌지 않는다.
    /// </summary>
    public class ToolFunctionCanonicalizationVersionWireTests
    {
        private const string W2SetV1 = "w2-set-v1";

        [Fact]
        public void Request_Descriptor_HasCanonicalizationVersionAtField8AsString()
        {
            var field = RegisterToolFunctionRequest.Descriptor.FindFieldByNumber(8);

            Assert.NotNull(field);
            Assert.Equal("canonicalization_version", field.Name);
            Assert.Equal(FieldType.String, field.FieldType);
            Assert.False(field.IsRepeated);
            Assert.Equal(RegisterToolFunctionRequest.CanonicalizationVersionFieldNumber, field.FieldNumber);
        }

        [Fact]
        public void Response_Descriptor_HasCanonicalizationVersionAtField4AsString()
        {
            var field = RegisterToolFunctionResponse.Descriptor.FindFieldByNumber(4);

            Assert.NotNull(field);
            Assert.Equal("canonicalization_version", field.Name);
            Assert.Equal(FieldType.String, field.FieldType);
            Assert.False(field.IsRepeated);
            Assert.Equal(RegisterToolFunctionResponse.CanonicalizationVersionFieldNumber, field.FieldNumber);
        }

        [Fact]
        public void Request_CanonicalizationVersion_SerializesAsField8LengthDelimited()
        {
            var request = new RegisterToolFunctionRequest { CanonicalizationVersion = W2SetV1 };

            // tag = (8 << 3) | wire type 2 = 0x42, length 9, UTF-8 "w2-set-v1"
            var expected = Concat(new byte[] { 0x42, 0x09 }, Encoding.UTF8.GetBytes(W2SetV1));
            Assert.Equal(expected, request.ToByteArray());

            var parsed = RegisterToolFunctionRequest.Parser.ParseFrom(expected);
            Assert.Equal(W2SetV1, parsed.CanonicalizationVersion);
        }

        [Fact]
        public void Response_CanonicalizationVersion_ParsesFromField4()
        {
            // tag = (4 << 3) | wire type 2 = 0x22
            var bytes = Concat(new byte[] { 0x22, 0x09 }, Encoding.UTF8.GetBytes(W2SetV1));

            var parsed = RegisterToolFunctionResponse.Parser.ParseFrom(bytes);

            Assert.Equal(W2SetV1, parsed.CanonicalizationVersion);
            Assert.Equal(bytes, parsed.ToByteArray());
        }

        [Fact]
        public void Response_FromServerWithoutVersion_LeavesCanonicalizationVersionEmpty()
        {
            // 912b23c 시점 server 응답: tool_function_digest(1)·presentation_revision_id(2)·cas_hash(3)만 있다.
            var legacy = new RegisterToolFunctionResponse
            {
                ToolFunctionDigest = "f",
                PresentationRevisionId = "p",
                CasHash = "h",
            };

            var parsed = RegisterToolFunctionResponse.Parser.ParseFrom(legacy.ToByteArray());

            Assert.Equal(string.Empty, parsed.CanonicalizationVersion);
            Assert.Equal("f", parsed.ToolFunctionDigest);
            Assert.Equal("p", parsed.PresentationRevisionId);
            Assert.Equal("h", parsed.CasHash);
        }

        [Fact]
        public void Request_UnsetCanonicalizationVersion_DoesNotChangeExistingFieldBytes()
        {
            var request = new RegisterToolFunctionRequest
            {
                RequestId = "r",
                BaseToolSpecDigest = "c",
                ImageDigest = "b",
            };

            // field 1/2/3 각각 tag, length 1, 값 — field 8 바이트는 없다.
            var expected = new byte[]
            {
                0x0A, 0x01, (byte)'r',
                0x12, 0x01, (byte)'c',
                0x1A, 0x01, (byte)'b',
            };
            Assert.Equal(expected, request.ToByteArray());
        }

        private static byte[] Concat(byte[] head, byte[] tail)
        {
            var result = new byte[head.Length + tail.Length];
            Buffer.BlockCopy(head, 0, result, 0, head.Length);
            Buffer.BlockCopy(tail, 0, result, head.Length, tail.Length);
            return result;
        }
    }
}
