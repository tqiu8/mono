using System;
using System.Threading;

public class ThreadTests {
	public static void SleepTest() {
		int count = 0;
		count += 5;
		Thread ThreadA = new Thread(MethodA);
		Thread ThreadB = new Thread(MethodB);
		Thread ThreadC = new Thread(MethodC);
		ThreadA.Start();
		ThreadB.Start();
		ThreadC.Start();

		void MethodA () {
			Thread.Sleep(1000);
			count += 10;
			Console.WriteLine("a finished");
		}

		void MethodB () {
			Thread.Sleep(4000);
			count += 20;
			Console.WriteLine("b finished");
		}

		void MethodC () {
			count += 30;
			Console.WriteLine("c finished");
		}
	}

	public static int IntAdd (int a, int b) {
		int c = a + b;
		int d = c + b;
		return d;
	}

}