# DeadlockInSocketsHandler
Repro project confirming deadlock in SocketsHttpHandler .Net Core version 2.1 and above for the issue https://github.com/dotnet/corefx/issues/32262

Compile the console app and start. It would produce output similar to:

Running the test...
No deadlocks detected: all requests completed.
No deadlocks detected: all requests completed.
No deadlocks detected: all requests completed.
No deadlocks detected: all requests completed.
No deadlocks detected: all requests completed.
No deadlocks detected: all requests completed.
No deadlocks detected: all requests completed.
No deadlocks detected: all requests completed.
No deadlocks detected: all requests completed.
Deadlock detected: 2 requests are not completed
Finished the test. Press any key to exit.

Because the deadlock is caused by a race condition, it would happen after a random number of the test repetitions.

You may then attach to the running process or dump it to investigate the threads.

There would be 2 deadlocked threads, for example, named "A" and "B".

Thread A. 
System.Private.CoreLib.dll!System.Threading.SpinWait.SpinOnce(int sleep1Threshold)
System.Private.CoreLib.dll!System.Threading.CancellationTokenSource.WaitForCallbackToComplete(long id)
System.Net.Http.dll!System.Net.Http.HttpConnectionPool.DecrementConnectionCount()
System.Net.Http.dll!System.Net.Http.HttpConnection.Dispose(bool disposing)
System.Net.Http.dll!System.Net.Http.HttpConnection.RegisterCancellation.AnonymousMethod__65_0(object s)
System.Private.CoreLib.dll!System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext executionContext, System.Threading.ContextCallback callback, object state)
System.Private.CoreLib.dll!System.Threading.CancellationTokenSource.ExecuteCallbackHandlers(bool throwOnFirstException)
System.Private.CoreLib.dll!System.Threading.CancellationTokenSource.ExecuteCallbackHandlers(bool throwOnFirstException)
DeadlockInSocketsHandler.dll!DeadlockInSocketsHandler.Program.DeadlockTestCore.AnonymousMethod__0() Line 83
System.Private.CoreLib.dll!System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext executionContext, System.Threading.ContextCallback callback, object state)
System.Private.CoreLib.dll!System.Threading.Tasks.Task.ExecuteWithThreadLocal(ref System.Threading.Tasks.Task currentTaskSlot)
System.Private.CoreLib.dll!System.Threading.ThreadPoolWorkQueue.Dispatch()

Thread B.
System.Net.Http.dll!System.Net.Http.HttpConnectionPool.GetConnectionAsync.AnonymousMethod__38_0(object s)
System.Private.CoreLib.dll!System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext executionContext, System.Threading.ContextCallback callback, object state)
System.Private.CoreLib.dll!System.Threading.CancellationTokenSource.ExecuteCallbackHandlers(bool throwOnFirstException)
System.Private.CoreLib.dll!System.Threading.CancellationTokenSource.ExecuteCallbackHandlers(bool throwOnFirstException)
DeadlockInSocketsHandler.dll!DeadlockInSocketsHandler.Program.DeadlockTestCore.AnonymousMethod__0() Line 83
System.Private.CoreLib.dll!System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext executionContext, System.Threading.ContextCallback callback, object state)
System.Private.CoreLib.dll!System.Threading.Tasks.Task.ExecuteWithThreadLocal(ref System.Threading.Tasks.Task currentTaskSlot)
System.Private.CoreLib.dll!System.Threading.ThreadPoolWorkQueue.Dispatch()


Explanation:

Thread A.

1. HttpConnectionPool.DecrementConnectionCount() entered lock(SyncObj)
2. Spin-waits in CancellationTokenSource.WaitForCallbackToComplete for Thread B to complete HttpConnectionPool.GetConnectionAsync.AnonymousMethod__38_0 callback

Thread B:

1. HttpConnectionPool.GetConnectionAsync.AnonymousMethod__38_0 callback waits to enter lock(SyncObj) that is held by Thread A
2. SyncObj can never be released Thread A because it is going to spin-wait infinitely unless Thread B makes progress.

Both threads cannot move, that confirms the deadlock.
