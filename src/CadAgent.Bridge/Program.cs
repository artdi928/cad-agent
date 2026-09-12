using System.Text.Json;
using CadAgent.Bridge;
using CadAgent.Protocol;

if (args.Length == 0)
{
    PrintUsage();
    return 2;
}

var command = args[0];

try
{
    var client = new PipeClient();

    // B1 compatibility & basic RPC commands
    if (command is "system.ping" or "cad.get_drawing_info" or "cad.list_layers" or "cad.list_blocks")
    {
        var response = await client.CallAsync(command);
        Console.WriteLine(JsonSerializer.Serialize(response, JsonDefaults.Options));
        return DetermineExitCode(command, response);
    }

    // Generic inspection: cad.list_entities or inspect or list-entities
    if (command is "cad.list_entities" or "inspect" or "list-entities")
    {
        var types = ParseTypesFromArgs(args.Skip(1).ToArray());
        JsonElement? parameters = types is { Count: > 0 }
            ? JsonSerializer.SerializeToElement(new { types }, JsonDefaults.Options)
            : null;

        var response = await client.CallAsync("cad.list_entities", parameters);
        Console.WriteLine(JsonSerializer.Serialize(response, JsonDefaults.Options));
        return DetermineExitCode("cad.list_entities", response);
    }

    // Change plan dry run: cad.validate_change_plan or plan validate
    if (command is "cad.validate_change_plan" ||
        (command == "plan" && args.Length > 1 && args[1] == "validate"))
    {
        var filePath = ExtractFilePath(command == "plan" ? args.Skip(2).ToArray() : args.Skip(1).ToArray());
        if (string.IsNullOrWhiteSpace(filePath))
        {
            Console.Error.WriteLine("Error: plan file path is required. Usage: cad.validate_change_plan [--file] <plan.json>");
            return 2;
        }

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Error: plan file '{filePath}' does not exist.");
            return 2;
        }

        JsonElement planElement;
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            planElement = JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: failed to parse plan JSON: {ex.Message}");
            return 2;
        }

        var parameters = JsonSerializer.SerializeToElement(new { plan = planElement }, JsonDefaults.Options);
        var response = await client.CallAsync("cad.validate_change_plan", parameters);
        Console.WriteLine(JsonSerializer.Serialize(response, JsonDefaults.Options));
        return DetermineExitCode("cad.validate_change_plan", response);
    }

    // Change plan apply: cad.apply_change_plan or plan apply
    if (command is "cad.apply_change_plan" ||
        (command == "plan" && args.Length > 1 && args[1] == "apply"))
    {
        var filePath = ExtractFilePath(command == "plan" ? args.Skip(2).ToArray() : args.Skip(1).ToArray());
        if (string.IsNullOrWhiteSpace(filePath))
        {
            Console.Error.WriteLine("Error: plan file path is required. Usage: cad.apply_change_plan [--file] <plan.json>");
            return 2;
        }

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Error: plan file '{filePath}' does not exist.");
            return 2;
        }

        JsonElement planElement;
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            planElement = JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: failed to parse plan JSON: {ex.Message}");
            return 2;
        }

        var parameters = JsonSerializer.SerializeToElement(new { plan = planElement }, JsonDefaults.Options);
        var response = await client.CallAsync("cad.apply_change_plan", parameters);
        Console.WriteLine(JsonSerializer.Serialize(response, JsonDefaults.Options));
        return DetermineExitCode("cad.apply_change_plan", response);
    }

    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintUsage();
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

static int DetermineExitCode(string method, RpcResponse response)
{
    if (!response.Ok)
    {
        var code = response.Error?.Code ?? "";
        if (IsConflictError(code)) return 1;
        return 2;
    }

    if (method == "cad.validate_change_plan" && response.Result.HasValue)
    {
        if (response.Result.Value.TryGetProperty("valid", out var validProp) &&
            !validProp.GetBoolean())
        {
            return 1;
        }
    }

    return 0;
}

static bool IsConflictError(string code) =>
    code is ErrorCodes.PreconditionFailed or
            ErrorCodes.WrongDocument or
            ErrorCodes.UnsavedDocument or
            ErrorCodes.DuplicateTarget or
            ErrorCodes.TargetNotFound or
            ErrorCodes.EntityTypeMismatch or
            ErrorCodes.UnsupportedField or
            ErrorCodes.UnsupportedOperation or
            ErrorCodes.PostconditionFailed or
            ErrorCodes.AtomicityViolation;

static List<string>? ParseTypesFromArgs(string[] remainingArgs)
{
    var types = new List<string>();
    for (var i = 0; i < remainingArgs.Length; i++)
    {
        var arg = remainingArgs[i];
        if (arg == "--types" && i + 1 < remainingArgs.Length)
        {
            types.AddRange(remainingArgs[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        else if (!arg.StartsWith('-'))
        {
            types.AddRange(arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
    }
    return types.Count > 0 ? types : null;
}

static string? ExtractFilePath(string[] remainingArgs)
{
    for (var i = 0; i < remainingArgs.Length; i++)
    {
        if (remainingArgs[i] == "--file" && i + 1 < remainingArgs.Length)
            return remainingArgs[i + 1];
        if (!remainingArgs[i].StartsWith('-'))
            return remainingArgs[i];
    }
    return null;
}

static void PrintUsage()
{
    Console.Error.WriteLine("Usage: CadAgent.Bridge <command> [options]");
    Console.Error.WriteLine("Commands:");
    Console.Error.WriteLine("  system.ping");
    Console.Error.WriteLine("  cad.get_drawing_info");
    Console.Error.WriteLine("  cad.list_layers");
    Console.Error.WriteLine("  cad.list_blocks");
    Console.Error.WriteLine("  cad.list_entities [--types dbtext,mtext,mleader,block]");
    Console.Error.WriteLine("  cad.validate_change_plan [--file] <plan.json>");
    Console.Error.WriteLine("  cad.apply_change_plan [--file] <plan.json>");
    Console.Error.WriteLine("  plan validate [--file] <plan.json>");
    Console.Error.WriteLine("  plan apply [--file] <plan.json>");
}
