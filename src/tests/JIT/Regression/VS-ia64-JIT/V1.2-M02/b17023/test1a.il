// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

.assembly extern System.Console
{
  .publickeytoken = (B0 3F 5F 7F 11 D5 0A 3A )
  .ver 4:0:0:0
}





// Metadata version: v1.0.1427
.assembly extern mscorlib
{
}
.assembly 'test1a'
{

  // --- The following custom attribute is added automatically, do not uncomment -------
  //  .custom instance void [mscorlib]System.Diagnostics.DebuggableAttribute::.ctor(bool,
  //                                                                                bool) = ( 01 00 00 01 00 00 ) 

  .hash algorithm 0x00008004
  .ver 0:0:0:0
}
.assembly extern xunit.core {}
// MVID: {268B9244-26E3-4F41-AE49-5311347AFB4B}
.imagebase 0x00400000
.subsystem 0x00000003
.file alignment 512
.corflags 0x00000001
// Image base: 0x02E00000

// =============== CLASS MEMBERS DECLARATION ===================

.class private sequential ansi sealed beforefieldinit Struct1
       extends [mscorlib]System.ValueType
{
    .field public int64    fld1
    .field public int64    fld2
} // end of class Struct1


.class private auto ansi beforefieldinit MyTest
       extends [mscorlib]System.Object
{
  .method public hidebysig static void  f(int32 x,
                                          valuetype Struct1 y) cil managed
  {
    // Code size       14 (0xe)
    .maxstack  2
    IL_0000:  ldarg.0
    IL_0001:  ldc.i4.0
    IL_0002:  ble.s      IL_000d

    IL_0004:  ldarg.0
    IL_0005:  ldc.i4.1
    IL_0006:  sub
    IL_0007:  ldarg.1
    IL_0008:  tail.
    IL_0009:  call       void MyTest::f(int32,
                                        valuetype Struct1)
    IL_000d:  ret
  } // end of method MyTest::f

  .method public hidebysig static int32 Main() cil managed
  {
    .custom instance void [xunit.core]Xunit.FactAttribute::.ctor() = (
        01 00 00 00
    )
    .entrypoint
    .locals init( valuetype Struct1 MYSTRUCT)
    // Code size       18 (0x12)
    .maxstack  2
    IL_0000:  ldc.i4.2
    IL_0001:  ldloc MYSTRUCT
    IL_0002:  call       void MyTest::f(int32,
                                        valuetype Struct1)
    IL_0007:  ldstr      "Pass!"
    IL_000c:  call       void [System.Console]System.Console::WriteLine(string)
    IL_0011:  ldc.i4 100		ret
  } // end of method MyTest::Main

  .method public hidebysig specialname rtspecialname 
          instance void  .ctor() cil managed
  {
    // Code size       7 (0x7)
    .maxstack  1
    IL_0000:  ldarg.0
    IL_0001:  call       instance void [mscorlib]System.Object::.ctor()
    IL_0006:  ret
  } // end of method MyTest::.ctor

} // end of class MyTest


// =============================================================


//*********** DISASSEMBLY COMPLETE ***********************
// WARNING: Created Win32 resource file test.res
