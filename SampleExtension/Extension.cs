﻿using System;

// plugin specific code goes here
namespace SampleExtension
{
    public class Extension
    {
        public static string ExtensionRun(string StartParam)
        {
            Console.WriteLine("in plugin user module - " + StartParam);
            return "OK";
        }
    }
}