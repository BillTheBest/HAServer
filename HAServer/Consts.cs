namespace HAServer
{
    public static class Consts
    {
        // Console error codes (passed to the command line when exiting)
        public enum ExitCodes : byte
        {
            OK = 0,                                             // Normal exit code
            ERR = 1                                             // Fatal error exit code
        }

        // Associate icon with category
        public struct CatStruc
        {
            public string name;
            public string icon;
        }

        // Define the running state of the HAServer
        public enum ServiceState : byte
        {
            STOPPED = 0,                                        // Not processing Messages and they are not saved on the message queue (all new messages lost)
            RUNNING = 1,                                        // Processing Messages as they come onto the queue
            PAUSED = 2                                          // Not processing Messages but they are backed up on the message queue
        }
    }
}
