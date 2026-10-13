using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace KRINT.Tests.Integration;

/// <summary>Opens an authenticated SignalR connection to one of KRINT's hubs.</summary>
internal static class HubClient
{
    public static async Task<HubConnection> ConnectAsync(KrintApi api, string hub)
    {
        var token = await api.EnsureTokenAsync();
        var connection = new HubConnectionBuilder()
            .WithUrl($"{api.Stack.AppUrl}/hubs/{hub}", o => o.AccessTokenProvider = () => Task.FromResult<string?>(token))
            .Build();
        connection.ServerTimeout = TimeSpan.FromMinutes(5);
        await connection.StartAsync();
        return connection;
    }
}
