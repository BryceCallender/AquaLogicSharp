using System.ComponentModel;
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
        Console.WriteLine("{0} = {1}", name, value);
    }
}

var aquaLogic = new AquaLogic();
var dataSource = new SocketDataSource("192.168.86.247", 8899);
await aquaLogic.Connect(dataSource);
Console.WriteLine("Connected!");
Console.WriteLine("To toggle a state, type in the State name, e.g. LIGHTS");
var thread = new Thread( () => aquaLogic.Process(DataChanged));
thread.Start();

try
{
    while (true)
    {
        var line = Console.ReadLine()?.ToUpper();

        if (line is null)
            continue;

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
finally
{
    dataSource.Disconnect();
}
