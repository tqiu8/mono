using System;
using System.Threading;

public class ThreadTests {
    public static void SleepTest() {
        ServerClass serverObject = new ServerClass();
        Thread ThreadA = new Thread(serverObject.MethodA);
        Thread ThreadB = new Thread(serverObject.MethodB);
        Thread ThreadC = new Thread(serverObject.MethodC);

        ThreadA.Start();
        ThreadB.Start();
        ThreadC.Start();
        
        Console.WriteLine("main thread ended ");
    }

    class ServerClass
    {
        static int count = 0;
        // The method that will be called when the thread is started.
        public void MethodA()
        {
            Thread.Sleep(1000);
            count += 10;
            Console.WriteLine("a finished");
        }

        public void MethodB()
        {
            Thread.Sleep(4000);
            count += 20;
            Console.WriteLine("b finished");
        }

        public void MethodC()
        {
            count += 30;
            Console.WriteLine("c finished");
        }
    }

}