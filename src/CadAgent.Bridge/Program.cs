using System.Text.Json;
using CadAgent.Bridge;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: CadAgent.Bridge <system.ping|cad.get_drawing_info|cad.list_layers|cad.list_blocks>");
    return 2;
}

try
{
    var response = await new PipeClient().CallAsync(args[0]);
    Console.WriteLine(JsonSerializer.Serialize(response, CadAgent.Protocol.JsonDefaults.Options));
    return response.Ok ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
