// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

.assembly extern System.Console { }
.assembly extern xunit.core {}
// MyBar is a normal reference type.
// MyBar::DoSelfBar explicitly overrides MyBar::DoBar
// DoSelfBar has a different signature than DoBar.
// Expect TypeLoadException when loading MyBar.

.assembly extern mscorlib{}
.assembly self_override3{}

.class public auto ansi beforefieldinit MyBar
       extends [mscorlib]System.Object
{
  .method public hidebysig newslot virtual 
          instance int32  DoBar()
  {
    .maxstack  1
    .locals init (int32 V_0)
    IL_0000:  ldstr      "In MyBar.DoBar"
    IL_0005:  call       void [System.Console]System.Console::WriteLine(string)
    IL_000a:  ldc.i4.4
    IL_000b:  stloc.0
    IL_000c:  br.s       IL_000e

    IL_000e:  ldloc.0
    IL_000f:  ret
  } // end of method MyBar::DoBar

  .method public hidebysig newslot virtual 
          instance int32  DoSelfBar(int32 i)
  {
    // Code size       16 (0x10)
    .maxstack  1
    .override MyBar::DoBar
    .locals init (int32 V_0)
    IL_0000:  ldstr      "In MyBar.DoSelfBar"
    IL_0005:  call       void [System.Console]System.Console::WriteLine(string)
    IL_000a:  ldc.i4.5
    IL_000b:  stloc.0
    IL_000c:  br.s       IL_000e

    IL_000e:  ldloc.0
    IL_000f:  ret
  }

  .method public hidebysig specialname rtspecialname 
          instance void  .ctor() cil managed
  {
    // Code size       7 (0x7)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  call       instance void [mscorlib]System.Object::.ctor()
    IL_0006:  ret
  } // end of method MyBar::.ctor

} // end of class MyBar

.class public auto ansi beforefieldinit CMain
       extends [mscorlib]System.Object
{

  .method public static int32 Go(){
    .maxstack 8
    .locals init (class MyBar V_1)
    newobj     instance void MyBar::.ctor()
    stloc.0
    ldloc.0
    callvirt   instance int32 MyBar::DoBar()
    ret
  }

  .method public hidebysig static int32  Main(string[] args) cil managed
  {
    .custom instance void [xunit.core]Xunit.FactAttribute::.ctor() = (
        01 00 00 00
    )
    .entrypoint
    // Code size       43 (0x2b)
    .maxstack  2
    .locals init (bool V_0, int32 V_2)

    IL_0000:  ldc.i4.1
    IL_0001:  stloc.0
    .try{
        call int32 CMain::Go()
        pop
        ldc.i4.0
        stloc.0 // pass=false because we shouldn't have returned from CMain::Go, expected TypeLoadException
        ldstr      "FAIL: did not catch expected TypeLoadException when loading MyBar."
        call       void [System.Console]System.Console::WriteLine(string)
        leave.s L1
    }
    catch [mscorlib]System.TypeLoadException{   
        ldstr "Caught expected TypeLoadException:"
        call       void [System.Console]System.Console::WriteLine(string)
        call       void [System.Console]System.Console::WriteLine(object)        
        leave.s L1
    }

    L1:
    IL_001d:  ldloc.0
    IL_001e:  brtrue.s   IL_0024

    IL_0020:  ldc.i4.s   101
    stloc.1
    ldstr "FAIL"
    call void [System.Console]System.Console::WriteLine(string)
    IL_0022:  br.s       IL_0029

    IL_0024:  ldc.i4.s   100
    IL_0026:  stloc.1
    ldstr "PASS"
    call void [System.Console]System.Console::WriteLine(string)
    IL_0027:  br.s       IL_0029

    IL_0029:  ldloc.1
    IL_002a:  ret
  } // end of method CMain::Main

  .method public hidebysig specialname rtspecialname 
          instance void  .ctor() cil managed
  {
    // Code size       7 (0x7)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  call       instance void [mscorlib]System.Object::.ctor()
    IL_0006:  ret
  } // end of method CMain::.ctor

} // end of class CMain
