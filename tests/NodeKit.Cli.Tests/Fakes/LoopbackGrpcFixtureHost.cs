using System;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NodeKit.Cli.Tests.Fakes
{
    /// <summary>
    /// P10.listener: <see cref="GrpcTestServer"/>와 같은 FakeBuildService/FakeToolRegistryService를 실제 Kestrel
    /// loopback 소켓(127.0.0.1, 임시 포트, HTTP/2 cleartext 전용)에 올린다. CLI는 주입 client 없이 `--url`
    /// <see cref="Url"/>만 받아 실제 GrpcChannel.ForAddress 경로로 붙는다. fake는 고정 응답만 돌려주므로 실제
    /// NodeVault 빌드·등록의 증거가 아니며, published executable subprocess 인수(P10.executable)도 아니다.
    /// </summary>
    internal sealed class LoopbackGrpcFixtureHost : IDisposable
    {
        private readonly IHost _host;

        public FakeBuildService Fake { get; }

        /// <summary>같은 listener의 ToolRegistryService 대역(P08 typed 등록).</summary>
        public FakeToolRegistryService Registry { get; }

        /// <summary>실제로 bind된 주소, 예: <c>http://127.0.0.1:41234</c>.</summary>
        public string Url { get; }

        public LoopbackGrpcFixtureHost()
        {
            Fake = new FakeBuildService();
            Registry = new FakeToolRegistryService();

            _host = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseKestrel(options =>
                            options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2))
                        .ConfigureServices(services =>
                        {
                            services.AddGrpc();
                            services.AddSingleton(Fake);
                            services.AddSingleton(Registry);
                        })
                        .Configure(app =>
                        {
                            app.UseRouting();
                            app.UseEndpoints(endpoints =>
                            {
                                endpoints.MapGrpcService<FakeBuildService>();
                                endpoints.MapGrpcService<FakeToolRegistryService>();
                            });
                        });
                })
                .Start();

            // port 0이면 Kestrel이 시작 뒤 실제 포트로 주소를 바꿔 둔다.
            var addresses = _host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException("Kestrel이 bind 주소를 알려주지 않았습니다.");
            Url = addresses.Addresses.Single();
        }

        public void Dispose() => _host.Dispose();
    }
}
