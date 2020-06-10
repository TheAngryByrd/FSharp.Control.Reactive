﻿module Control.Reactive.Tests.SchedulerSpecs

open System
open System.Collections.Generic
open System.Reactive.Concurrency
open NUnit.Framework
open FSharp.Control.Reactive.Testing
open FSharp.Control.Reactive
open System.Threading
open System.Reactive
open Microsoft.Reactive.Testing
open System.Reactive.Disposables
open FSharp.Control.Reactive.Scheduler

let equal x y = Assert.AreEqual (x, y)
let isTrue x = Assert.True (x : bool)

[<Test>]
let ``schedule immediate non-recursive action`` () =
    let mutable res = false
    Scheduler.Immediate
    |> Schedule.action (fun () -> res <- true)
    |> Disposable.ignoring (fun () -> isTrue res)

[<Test>]
let ``schedule immediate recursive action`` () =
    let i = ref 0
    Scheduler.Immediate
    |> Schedule.actionRec (fun f -> incr i; if !i < 10 then f ())
    |> Disposable.ignoring (fun () -> equal !i 10)

[<Test>]
let ``schedule immediate action`` () =
    let x = ref 0
    Scheduler.Immediate
    |> Schedule.funcSpan 42 TimeSpan.Zero (fun s y -> x := y; Disposable.Empty)
    |> Disposable.ignoring (fun () -> equal 42 !x)

[<Test>]
let ``schedule long-running`` () =
    let x, e = WaitHandle.Signal, WaitHandle.Signal
    TaskPoolScheduler.Default
    |> Scheduler.AsLongRunning
    |> Schedule.actionLongState 42 (fun _ c -> 
        while not c.IsDisposed do WaitHandle.flag x
        WaitHandle.flag e)
    |> Disposable.ignoring x.WaitOne
    WaitHandle.wait e

[<Test>]
let ``schedule periodic 1`` () =
    let n = ref 0
    let e = WaitHandle.Signal
    Scheduler.Default
    |> Schedule.periodicAction 
        (TimeSpan.FromMilliseconds 50.) 
        (fun () -> incr n; if !n = 10 then WaitHandle.flag e)
    |> Disposable.ignoring e.WaitOne

[<Test>]
let ``catch built-in swallow shallow`` () =
    let finish = WaitHandle.Signal
    Scheduler.Default
    |> Schedule.catch (fun _ -> WaitHandle.flag finish; true)
    |> Schedule.action (fun () -> failwith "Something happend!")
    |> Disposable.ignoring finish.WaitOne

[<Test>]
let ``schedule async`` () =
    TestSchedule.usage <| fun sch ->
        let o = sch.CreateObserver<int> ()
        sch |> Schedule.async (fun s ct -> async { o.OnNext 42 })
            |> ignore
        sch.Start ()
        equal (TestObserver.nexts o) [42]

[<Test>]
let ``schedule async with due time`` () =
    TestSchedule.usage <| fun sch ->
        let o = sch.CreateObserver<int> ()
        sch |> Schedule.asyncSpanUnit 
                (TimeSpan.FromTicks 50L) 
                (fun s ct -> async { o.OnNext 42 })
            |> Disposable.ignoring sch.Start
        equal (TestObserver.nexts o) [42]

[<Test>]
[<Ignore("sleep scheduler test hangs")>]
let ``schedule sleep cancel`` () =
    let e = WaitHandle.Signal
    let cts = new CancellationTokenSource ()
    Scheduler.Default
    |> Schedule.sleepCancel (TimeSpan.FromHours 1.) cts.Token
    |> fun a -> async.TryFinally(a, fun () -> WaitHandle.flag e)
    |> Async.Ignore
    |> Async.Start
    cts.Cancel ()
    WaitHandle.wait e

[<Test>]
let ``schedule async without cancellation`` () =
    TestSchedule.usage <| fun sch ->
        let o = sch.CreateObserver<int> ()
        sch |> Schedule.async (fun s _ -> async { 
            o.OnNext 42
            do! Schedule.yield_ s
            o.OnNext 43
            do! Schedule.sleep (TimeSpan.FromTicks 10L) s
            o.OnNext 44
            do! Schedule.sleepOffset (new DateTimeOffset (250L, TimeSpan.Zero)) s
            o.OnNext 45 })
            |> Disposable.ignoring sch.Start

        equal (TestObserver.nexts o) [42..45]

[<Test>]
let ``schedule with disabled optimazations isn't long-running`` () =
    TaskPoolScheduler.Default
    |> Schedule.disableOptimizations
    |> Scheduler.asLongRunning
    |> equal None

let days i = (new DateTimeOffset(1979, 10, 31, 4, 30, 15, TimeSpan.Zero)).AddDays (float i)
let stamped x stamp = new Timestamped<_> (x, stamp)

let schedule (xs : List<_>) x y (sch : IScheduler) = 
    Schedule.actionOffset (days x) (fun () -> xs.Add (stamped y sch.Now)) sch

[<Test>]
let ``schedule historical advanced-by`` () =
    let xs = new List<Timestamped<int>> ()
    let sch = Scheduler.Historical
    sch
    |> Schedule.multiple (
        [0; 1; 2; 10; 11] 
        |> List.map (fun i -> schedule xs i i))
    |> Disposable.ignoring (fun () -> (days 8 - sch.Now) |> sch.AdvanceBy)
    [0..2]
    |> List.map (fun i -> stamped i (days i))
    |> equal xs