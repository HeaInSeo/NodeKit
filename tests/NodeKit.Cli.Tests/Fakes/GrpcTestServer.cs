using System;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NodeKit.Cli.Tests.Fakes
{
    /// <summary>
    /// FakeBuildService를 in-process(ASP.NET Core TestServer)로 호스팅하고,
    /// 그 서버에 직접 붙는 GrpcChannel을 제공한다. 실제 소켓 바인딩 없이
    /// 진짜 gRPC 직렬화/전송 코드를 그대로 태운다.
    /// </summary>
    internal sealed class GrpcTestServer : IDisposable
    {
        private readonly IHost _host;

        public FakeBuildService Fake { get; }

        public GrpcChannel Channel { get; }

        public GrpcTestServer()
        {
            Fake = new FakeBuildService();

            _host = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseTestServer()
                        .ConfigureServices(services =>
                        {
                            services.AddGrpc();
                            services.AddSingleton(Fake);
                        })
                        .Configure(app =>
                        {
                            app.UseRouting();
                            app.UseEndpoints(endpoints => endpoints.MapGrpcService<FakeBuildService>());
                        });
                })
                .Start();

            Channel = GrpcChannel.ForAddress(
                "http://localhost",
                new GrpcChannelOptions { HttpHandler = _host.GetTestServer().CreateHandler() });
        }

        public void Dispose()
        {
            Channel.Dispose();
            _host.Dispose();
        }
    }
}
