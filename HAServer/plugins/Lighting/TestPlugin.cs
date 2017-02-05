using System;
using System.Collections.Generic;
using System.Text;

namespace TestPlugin
{
    public class MyPlugin
    {
        private object _host;

        public string Program(object pubSub)
        {
            try
            {
                var yy = 0;
                var tt = 1 / yy;
                _host = pubSub;
                Console.WriteLine("In Plugin");
                return "OK";

            }
            catch (Exception ex)
            {
                return ex.ToString();
            }
        }
    }
}