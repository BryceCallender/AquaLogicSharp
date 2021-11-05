﻿using System.ComponentModel;
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
aquaLogic.Connect(new SocketDataSource("192.168.86.247", 8899));
Console.WriteLine("Connected!");
Console.WriteLine("To toggle a state, type in the State name, e.g. LIGHTS");
var thread = new Thread(() => aquaLogic.Process(DataChanged));
thread.Start();

while (true)
{
    var line = Console.ReadLine()?.ToUpper();
    
    if (line is null)
        continue;
    
    if (line is "RIGHT")
        aquaLogic.SendKey(Key.RIGHT);
    
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