using System.IO;
using Grpc.Core;
using NodeKit.Cli.Operations;
using NodeKit.Cli.Tests.Fakes;
using Xunit;

namespace NodeKit.Cli.Tests
{
    /// <summary>
    /// P10.executable (S4-04-F, ToolFunction): 실제 published package의 <c>nodekit</c> process로 P08 scenario를 돈다.
    /// production의 `function-recipe submit`은 상류 build gate가 닫혀 있어(UpstreamAcceptsToolFunctionBuild=false)
    /// published process에서는 RPC 없이 거부되는 것까지만 확인한다. ACK 유실 receipt는 gate만 연 테스트 경로가
    /// 같은 loopback listener에 만들고, replay는 published process가 주입 client 없이 보낸다. fake 응답이므로
    /// NodeVault 등록 증거가 아니다.
    /// </summary>
    public partial class ToolFunctionRegistryP08Tests
    {
        [Fact]
        public void Published_FunctionRecipeSubmit_GateClosed_Exit1_NoRpc_NoReceipt()
        {
            var package = PublishedCliPackage.Require();
            using var host = NewLoopbackHost();
            Save(ReadyRecipe());

            var result = package.Run(_workDir, new[] { "function-recipe", "submit", "recipe.json", "--url", host.Url });

            Assert.Equal(1, result.Exit);
            Assert.Contains(ToolFunctionRecipeSubmitCommand.UpstreamGateClosedCode, result.Stderr);
            Assert.Empty(host.Fake.CallOrder);
            Assert.Empty(host.Registry.RegisterRequests);
            Assert.False(Directory.Exists(Path.Join(_workDir, ".nodekit", "registrations")));
        }

        [Fact]
        public void Published_RegisterReplayAfterAckLoss_SameBytesAndRequestId_NoNewBuild()
        {
            var package = PublishedCliPackage.Require();
            using var host = NewLoopbackHost();
            host.Registry.OnRegisterToolFunction = _ => throw new RpcException(new Status(StatusCode.Unavailable, "ack lost: fixture"));

            var (exit, _, stderr) = SubmitOverLoopback(host, Save(ReadyRecipe()));

            Assert.Equal(1, exit);
            Assert.Contains(ToolFunctionRegistrar.UnknownCode, stderr);
            Assert.Equal(RegistrationState.Unknown, ReadRawReceipt(RegistrationPath()).State);
            var buildCalls = host.Fake.CallOrder.Count;
            host.Registry.OnRegisterToolFunction = _ => Response(ServerF, ServerH);

            var replay = package.Run(
                _workDir,
                new[] { "function-recipe", "register-replay", Path.Join(".nodekit", "registrations", RegistrationId + ".json"), "--url", host.Url, "--format", "jsonl" });

            Assert.True(replay.Exit == 0, replay.Stderr);
            Assert.Equal(2, host.Registry.RegisterRequests.Count);
            Assert.Equal(host.Registry.RegisterRequestBytes[0], host.Registry.RegisterRequestBytes[1]);
            Assert.Equal(RegistrationId, host.Registry.RegisterRequests[1].RequestId);
            Assert.Equal(buildCalls, host.Fake.CallOrder.Count);
            var completed = Completed(replay.Stdout);
            Assert.Equal(ServerH, completed.GetProperty("cas_hash").GetString());
            Assert.Equal(ServerF, completed.GetProperty("tool_function_digest").GetString());
            var receipt = ReadRawReceipt(RegistrationPath());
            Assert.Equal(RegistrationState.Registered, receipt.State);
            Assert.Equal(2, receipt.Attempts);
            Assert.Equal(host.Url, receipt.Endpoint);
        }
    }
}
