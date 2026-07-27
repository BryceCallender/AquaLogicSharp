using System.ComponentModel;
using System.Text.Json;
using AquaLogicSharp;
using AquaLogicSharp.Implementation;
using AquaLogicSharp.Models;
using EnumsNET;

void DataChanged(AquaLogic aquaLogic)
{
    foreach(PropertyDescriptor descriptor in TypeDescriptor.GetProperties(aquaLogic))
    {
        var name = descriptor.Name;
        var value = descriptor.GetValue(aquaLogic);
        Console.WriteLine("{0} = {1}", name, JsonSerializer.Serialize(value));
    }
}

using var aquaLogic = new AquaLogic();
var dataSource = new SocketDataSource("192.168.1.169", 8899); // USR-W610
await aquaLogic.Connect(dataSource);
Console.WriteLine("Connected!");
Console.WriteLine("To toggle a state, type in the State name, e.g. LIGHTS");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true; // unwind through the finally instead of being killed
    cts.Cancel();
};

// Keep the task so faults surface and shutdown can wait on it, rather than
// handing it to a Thread that returns at the first await and drops it.
var processTask = aquaLogic.Process(DataChanged, cts.Token);

try
{
    while (!cts.IsCancellationRequested)
    {
        var line = Console.ReadLine()?.ToUpper();

        // null means stdin closed; continuing here just spun the CPU
        if (line is null)
            break;

        // var result = Enums.TryParse(line, out Key key);
        //
        // if (result)
        // {
        //     aquaLogic.SendKey(key);
        //     continue;
        // }

        try
        {
            var state = Enums.Parse<State>(line);
            aquaLogic.SetState(state, !aquaLogic.GetState(state));
        }
        catch (Exception)
        {
            Console.Error.WriteLine($"Invalid state name {line}");
        }
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
}
finally
{
    cts.Cancel();
    dataSource.Disconnect(); // unblocks the pending socket read
    await processTask;
}
