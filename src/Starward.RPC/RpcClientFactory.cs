using Grpc.Core;
using Grpc.Net.Client;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.RPC;

public static class RpcClientFactory
{



    public static bool CheckRpcServerRunning()
    {
        using Mutex mutex = new(true, AppConfig.MutexAndPipeName, out bool createdNew);
        return !createdNew;
    }




    public static async Task<Process> EnsureRpcServerRunningAsync()
    {
        Process? process = null;
        if (!CheckRpcServerRunning())
        {
            string name = Path.Combine(AppContext.BaseDirectory, "Starward.RPC.exe");
            process = Process.Start(new ProcessStartInfo
            {
                FileName = name,
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true,
                Arguments = $"{AppConfig.StartupMagic} {Environment.ProcessId}",
            });
        }
        try
        {
            var client = new Env.Env.EnvClient(CreateChannel());
            var info = await client.GetRpcServerInfoAsync(new EmptyMessage(), deadline: DateTime.UtcNow.AddSeconds(5));
            return process ?? Process.GetProcessById(info.ProcessId);
        }
        catch (RpcException ex) when (ex.Status is { StatusCode: StatusCode.DeadlineExceeded })
        {
            throw new TimeoutException("Checking RPC server timed out.");
        }
    }




    private static GrpcChannel CreateChannel()
    {
        var connectionFactory = new NamedPipesConnectionFactory(AppConfig.MutexAndPipeName);
        var socketsHttpHandler = new SocketsHttpHandler
        {
            ConnectCallback = connectionFactory.ConnectAsync,
            // 防止因为系统代理无法建立连接
            UseProxy = false,
        };
        return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = socketsHttpHandler
        });
    }




    public static T CreateRpcClient<T>() where T : ClientBase<T>
    {
        return (T)Activator.CreateInstance(typeof(T), CreateChannel())!;
    }



}
