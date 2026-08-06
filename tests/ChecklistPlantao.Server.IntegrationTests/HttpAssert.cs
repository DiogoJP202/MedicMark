using System.Net.Http.Json;

namespace ChecklistPlantao.Server.IntegrationTests;

/// <summary>
/// <c>EnsureSuccessStatusCode</c> esconde o motivo da falha. Como a API responde
/// <c>ProblemDetails</c>, incluir o corpo na mensagem transforma "409 Conflict" em algo acionável.
/// </summary>
internal static class HttpAssert
{
    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();

        Assert.Fail($"{(int)response.StatusCode} {response.StatusCode} em {response.RequestMessage?.RequestUri}\n{body}");
    }
}
