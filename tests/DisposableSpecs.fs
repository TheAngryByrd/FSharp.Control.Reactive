﻿module FSharp.Reactive.Tests.DisposableSpecs

open NUnit.Framework
open FsCheck
open FSharp.Control.Reactive
open FSharp.Control.Reactive.Testing
open System.Reactive.Disposables

[<Test>]
let ``Disposable.compose is immutable`` () =    
    let disp1 = Disposable.create id
    let disp2 = Disposable.create id
    let disp3 = Disposable.create id
    
    let groupa = Disposable.compose disp1 disp2
    let groupb = Disposable.compose disp1 disp2
    let groupc = Disposable.compose groupa disp3

    //groupa isn't mutated to groupc
    //previously, this test would have failed
    Assert.That(groupa, Is.Not.EqualTo(groupc))


[<Test>]
let ``Disposable.compose follows ordering`` () =    
    let mutlist = new ResizeArray<_>()
    let add n = fun () -> mutlist.Add n
    let contents () = mutlist |> List.ofSeq

    let range = [1..10] 

    //apply compose recursively over range
    let composite =
        range
        |> List.map add
        |> List.map Disposable.create 
        |> List.reduce (fun a b -> a |> Disposable.compose b)
    
    //dispose the entire range
    composite.Dispose()

    Assert.That(contents (), Is.EqualTo(range))
    
[<Test>]
let ``Disposals happen only once`` () =    
        let mutlist = new ResizeArray<_>()
        let add n = fun () -> mutlist.Add n
        let contents () = mutlist |> List.ofSeq

        let disp1 = Disposable.create (add 1)
        let disp2 = Disposable.create (add 2)
        let disp3 = Disposable.create (add 3)

        let group1 = disp1 |> Disposable.compose disp2
        let group2 = group1 |> Disposable.compose disp3

        group1.Dispose()
        //group1 isn't mutated and disposes in order
        Assert.That(contents (), Is.EqualTo([1; 2;]))
        
        group2.Dispose()
        //group1 has already been disposed,
        //so only disp3.Dispose() should happen
        Assert.That(contents (), Is.EqualTo([1; 2; 3]))
        ()
    
[<Test>]
let ``CompositeDispose disposes correctly inner disposables`` () =    
        let mutlist = new ResizeArray<_>()
        let add n = fun () -> mutlist.Add n
        let contents () = mutlist |> List.ofSeq

        let compDisp = new CompositeDisposable()
        let disp1 = Disposable.create (add 1)
        let disp2 = Disposable.create (add 2)
        let disp3 = Disposable.create (add 3)

        disp1 |> Disposable.disposeWith compDisp
        disp2 |> Disposable.disposeWith compDisp
        disp3 |> Disposable.disposeWith compDisp

        compDisp.Dispose()
        
        Assert.That(contents (), Is.EqualTo([1; 2; 3;]))
        ()

