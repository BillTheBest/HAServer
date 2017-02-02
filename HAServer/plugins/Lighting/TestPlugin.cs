using System;
using System.Collections.Generic;
using System.Text;

namespace Plugins
{
    public class MyPlugin
    {
        public void Program(string param)
        {
            Console.WriteLine($"you said '{param}!'");
        }
    }
}