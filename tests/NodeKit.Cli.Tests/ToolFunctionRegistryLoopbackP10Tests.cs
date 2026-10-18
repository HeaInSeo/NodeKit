using System;
using System.IO;
using System.Linq;
using Grpc.Core;
using NodeKit.Cli.Operations;
using NodeKit.Cli.Tests.Fakes;
using NodeKit.Grpc;
using Xunit;
using Pb = Nodevault.V1;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P10.listener: P08 build→register scenario를 in-process TestServer 대신 실제 loopback HTTP/2 소켓
    /// (<see cref="LoopbackGrpcFixtureHost"/>)으로 보낸다. client는 `--url`과 같은 주소 문자열로 만든 실제
    /// GrpcChannel이고, replay는 주입 client 없이 CliApp 경로로 간다. fake 응답이므로 NodeVault 등록 증거가 아니다.
    /// </summary>
    public partial class ToolFunctionRegistryP08Tests
    {
        // 같은 scenario·ID로 in-process와 loopback을 각각 돌리면 서버가 받은 요청 transcript가 같아야 한다.
        [Fact]
        public void Loopback_SameScenarioAsInProcess_SameTranscriptAndResult()
        {
            var inProcessDir = Directory.CreateDirectory(Path.Join(_workDir, "in-process")).FullName;
            var loopbackDir = Directory.CreateDirectory(Path.Join(_workDir, "loopback")).FullName;

            using var server = NewServer();
            server.Registry.OnRegisterToolFunction = _ => Response(ServerF, ServerH);
            var (inExit, _, inErr) = Submit(server, SaveIn(inProcessDir, ReadyRecipe()));
            Assert.True(inExit == 0, inErr);

            using var host = NewLoopbackHost();
            host.Registry.OnRegisterToolFunction = _ => Response(ServerF, ServerH);
            var (exit, stdout, stderr) = SubmitOverLoopback(host, SaveIn(loopbackDir, ReadyRecipe()), "--format", "jsonl");

            Assert.True(exit == 0, stderr);
            Assert.Equal(new[] { "Resolve", "Submit", "Watch" }, host.Fake.CallOrder);
            Assert.Equal(server.Fake.CallOrder, host.Fake.CallOrder);
            // requested_at은 client 벽시계라 실행마다 다르다. 그 한 필드만 지우고 나머지 bytes 의미를 비교한다.
            Assert.Equal(server.Fake.ResolveRequests.Select(WithoutRequestedAt), host.Fake.ResolveRequests.Select(WithoutRequestedAt));
            Assert.Equal(server.Fake.SubmitRequests, host.Fake.SubmitRequests);
            Assert.Equal(server.Fake.WatchRequests, host.Fake.WatchRequests);
            Assert.Equal(Assert.Single(server.Registry.RegisterRequestBytes), Assert.Single(host.Registry.RegisterRequestBytes));

            var completed = Completed(stdout);
            Assert.Equal("Succeeded", completed.GetProperty("status").GetString());
            Assert.Equal(ServerH, completed.GetProperty("cas_hash").GetString());
            Assert.Equal(ServerF, completed.GetProperty("tool_function_digest").GetString());

            var receipt = ReadRawReceipt(Path.Join(loopbackDir, ".nodekit", "registrations", RegistrationId + ".json"));
            Assert.Equal(RegistrationState.Registered, receipt.State);
            Assert.Equal(host.Url, receipt.Endpoint);
            Assert.Equal(DigestB, receipt.Provenance.FunctionImageDigest);
        }

        // ACK 유실 뒤 주입 client 없는 실제 CLI(`function-recipe register-replay ... --url`)가 같은 listener에
        // 저장된 같은 bytes·request ID를 다시 보낸다. 빌드 RPC는 다시 가지 않는다.
        [Fact]
        public void Loopback_AckLostThenCliReplayOverSocket_SameBytes_NoNewBuild()
        {
            using var host = NewLoopbackHost();
            host.Registry.OnRegisterToolFunction = _ => throw new RpcException(new Status(StatusCode.Unavailable, "ack lost: fixture"));

            var (exit, _, stderr) = SubmitOverLoopback(host, Save(ReadyRecipe()));

            Assert.Equal(1, exit);
            Assert.Contains(ToolFunctionRegistrar.UnknownCode, stderr);
            Assert.Equal(RegistrationState.Unknown, ReadRawReceipt(RegistrationPath()).State);
            var buildCalls = host.Fake.CallOrder.Count;
            host.Registry.OnRegisterToolFunction = _ => Response(ServerF, ServerH);

            using var replayOut = new StringWriter();
            using var replayErr = new StringWriter();
            var replayExit = CliApp.Run(
                new[] { "function-recipe", "register-replay", RegistrationPath(), "--url", host.Url, "--format", "jsonl" },
                replayOut,
                replayErr);

            Assert.True(replayExit == 0, replayErr.ToString());
            Assert.Equal(2, host.Registry.RegisterRequests.Count);
            Assert.Equal(host.Registry.RegisterRequestBytes[0], host.Registry.RegisterRequestBytes[1]);
            Assert.Equal(RegistrationId, host.Registry.RegisterRequests[1].RequestId);
            Assert.Equal(buildCalls, host.Fake.CallOrder.Count);
            Assert.Equal(ServerH, Completed(replayOut.ToString()).GetProperty("cas_hash").GetString());
            var receipt = ReadRawReceipt(RegistrationPath());
            Assert.Equal(RegistrationState.Registered, receipt.State);
            Assert.Equal(2, receipt.Attempts);
            Assert.Equal(host.Url, receipt.Endpoint);
        }

        private static Pb.ToolSpecRequest WithoutRequestedAt(Pb.ToolSpecRequest request)
        {
            var copy = request.Clone();
            copy.RequestedAt = 0;
            return copy;
        }

        private static LoopbackGrpcFixtureHost NewLoopbackHost()
        {
            var host = new LoopbackGrpcFixtureHost();
            ArrangeBuild(host.Fake);
            return host;
        }

        // 상류 build gate는 아직 닫혀 있어(UpstreamAcceptsToolFunctionBuild=false) gate만 테스트에서 연다.
        // client는 주입하지만 둘 다 `--url`과 같은 주소 문자열로 만든 실제 소켓 channel이다.
        private static (int Exit, string Stdout, string Stderr) SubmitOverLoopback(
            LoopbackGrpcFixtureHost host, string recipePath, params string[] extraArgs)
        {
            using var client = new GrpcToolSpecClient(host.Url);
            using var registry = new GrpcToolFunctionRegistryClient(host.Url);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var args = new[] { "function-recipe", "submit", recipePath, "--url", host.Url }.Concat(extraArgs).ToArray();
            var exit = ToolFunctionRecipeSubmitCommand.Run(
                args, stdout, stderr, client, () => BuildRequestId, toolFunctionBuildGateOpen: true, registryClient: registry, registrationRequestIdProvider: () => RegistrationId);
            return (exit, stdout.ToString(), stderr.ToString());
        }
    }
}
