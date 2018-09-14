using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DeadlockInSocketsHandler
{
    /// <summary>
    /// Test application confirming deadlock in SocketsHttpHandler SDK 2.1.0 and above
    /// </summary>
    class Program
    {
        const int MaximumConnectionsPerServer = 1;
        const int MaxRequestCount = MaximumConnectionsPerServer * 2;

        static void Main(string[] args)
        {
            Console.WriteLine("Running the test...");
            DeadlockTest();

            Console.WriteLine("Finished the test. Press any key to exit.");
            Console.ReadKey();
        }

        static void DeadlockTest()
        {
            var (openSockets, port) = StartMockServerThatNeverResponds();

            try
            {
                // Run test core a few times. The deadlock is likely but may not happen on a first try.
                while (true)
                {
                    if (DeadlockTestCore(port))
                    {
                        break;
                    }
                }
            }
            finally
            {
                StopMockServer(openSockets);
            }
        }

        static bool DeadlockTestCore(int port)
        {
            var httpClient = new HttpClient(new HttpClientHandler
            {
                MaxConnectionsPerServer = MaximumConnectionsPerServer
            });

            long preparedRequestCount = 0;

            using (var cancelSendAsync = new ManualResetEvent(false))
            using (var startSendAsync = new ManualResetEvent(false))
            {
                var invokeTasks = new List<Task>();

                for (var i = 0; i < MaxRequestCount; i++)
                {
                    invokeTasks.Add(Task.Run(() =>
                    {
                        Interlocked.Increment(ref preparedRequestCount);
                        using (var cts = new CancellationTokenSource())
                        {
                            try
                            {
                                using (var httpRequestMessage = new HttpRequestMessage(new HttpMethod("POST"), new Uri($"http://127.0.0.1:{port}")))
                                {
                                    httpRequestMessage.Content = new StringContent("ABC");

                                    // Ready, Set
                                    startSendAsync.WaitOne();
                                    
                                    // Go!
                                    httpClient.SendAsync(httpRequestMessage, cts.Token).ConfigureAwait(false);

                                    // Ready, Set
                                    cancelSendAsync.WaitOne();

                                    // Go!
                                    cts.Cancel();
                                }
                            }
                            catch (OperationCanceledException)
                            {
                            }
                        }
                    }));
                }

                // Wait while the worker threads are being initialized.
                while (MaxRequestCount != Interlocked.Read(ref preparedRequestCount))
                {
                    Thread.Yield();
                }

                // Send in parallel at the same moment.
                startSendAsync.Set();

                // Wait a second
                Thread.Sleep(1000);

                // Cancel all requests at once.
                cancelSendAsync.Set();

                // All tasks shoudl finish shortly unlessf there is a delay/deadlock
                Task.WaitAll(invokeTasks.ToArray(), MaxRequestCount * 2000);

                var unfinishedTasks = new List<Task>();
                foreach (var t in invokeTasks)
                {
                    if (!t.IsCompleted)
                    {
                        unfinishedTasks.Add(t);
                    }
                }

                if (unfinishedTasks.Count > 0)
                {
                    Console.WriteLine($"Deadlock detected: {unfinishedTasks.Count} requests are not completed");
                    return true;
                }

                Console.WriteLine($"No deadlocks detected: all requests completed.");
                return false;
            }
        }

        static void StopMockServer(IEnumerable<Socket> openSockets)
        {
            lock (openSockets)
            {
                foreach (var s in openSockets)
                {
                    try
                    {
                        s.Shutdown(SocketShutdown.Both);
                    }
                    catch (SocketException)
                    {
                    }
                    catch (ObjectDisposedException)
                    {
                        continue;
                    }
                    s.Close();
                }
            }
        }

        static (IEnumerable<Socket> openSockets, int port) StartMockServerThatNeverResponds()
        {
            ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
            ThreadPool.SetMinThreads(150, completionPortThreads);

            var listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listenSocket.Bind(new IPEndPoint(IPAddress.Any, 0));
            listenSocket.Listen(200);

            var sockets = new List<Socket>() { listenSocket };
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    while (true)
                    {
                        var socket = listenSocket.Accept();
                        lock (sockets)
                        {
                            sockets.Add(socket);
                        }
                    }
                }
                catch (Exception ex) when (ex is ObjectDisposedException || ex is SocketException)
                {
                    // listenSocket is closed
                }
            });
            return (sockets, ((IPEndPoint)listenSocket.LocalEndPoint).Port);
        }
    }
}
