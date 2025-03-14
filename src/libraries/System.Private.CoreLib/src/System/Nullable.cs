// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Runtime.Versioning;

#pragma warning disable CA1066 // Implement IEquatable when overriding Object.Equals

namespace System
{
    // Because we have special type system support that says a boxed Nullable<T>
    // can be used where a boxed<T> is use, Nullable<T> can not implement any intefaces
    // at all (since T may not).   Do NOT add any interfaces to Nullable!
    //
    [Serializable]
    [NonVersionable] // This only applies to field layout
    [System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public partial struct Nullable<T> where T : struct
    {
        private readonly bool hasValue; // Do not rename (binary serialization)
        internal T value; // Do not rename (binary serialization) or make readonly (can be mutated in ToString, etc.)

        [NonVersionable]
        public Nullable(T value)
        {
            this.value = value;
            hasValue = true;
        }

        public readonly bool HasValue
        {
            [NonVersionable]
            get => hasValue;
        }

        public readonly T Value
        {
            get
            {
                if (!hasValue)
                {
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_NoValue();
                }
                return value;
            }
        }

        [NonVersionable]
        public readonly T GetValueOrDefault() => value;

        [NonVersionable]
        public readonly T GetValueOrDefault(T defaultValue) =>
            hasValue ? value : defaultValue;

        public override bool Equals(object? other)
        {
            if (!hasValue) return other == null;
            if (other == null) return false;
            return value.Equals(other);
        }

        public override int GetHashCode() => hasValue ? value.GetHashCode() : 0;

        public override string? ToString() => hasValue ? value.ToString() : "";

        [NonVersionable]
        public static implicit operator Nullable<T>(T value) =>
            new Nullable<T>(value);

        [NonVersionable]
        public static explicit operator T(Nullable<T> value) => value!.Value;
    }

    public static class Nullable
    {
        public static int Compare<T>(Nullable<T> n1, Nullable<T> n2) where T : struct
        {
            if (n1.HasValue)
            {
                if (n2.HasValue) return Comparer<T>.Default.Compare(n1.value, n2.value);
                return 1;
            }
            if (n2.HasValue) return -1;
            return 0;
        }

        public static bool Equals<T>(Nullable<T> n1, Nullable<T> n2) where T : struct
        {
            if (n1.HasValue)
            {
                if (n2.HasValue) return EqualityComparer<T>.Default.Equals(n1.value, n2.value);
                return false;
            }
            if (n2.HasValue) return false;
            return true;
        }

        // If the type provided is not a Nullable Type, return null.
        // Otherwise, returns the underlying type of the Nullable type
        public static Type? GetUnderlyingType(Type nullableType!!)
        {
#if CORERT
            // This is necessary to handle types without reflection metadata
            if (nullableType.TryGetEEType(out EETypePtr nullableEEType))
            {
                if (nullableEEType.IsGeneric)
                {
                    if (nullableEEType.IsNullable)
                    {
                        return Type.GetTypeFromEETypePtr(nullableEEType.NullableType);
                    }
                }
                return null;
            }
#endif

            if (nullableType.IsGenericType && !nullableType.IsGenericTypeDefinition)
            {
                // instantiated generic type only
                Type genericType = nullableType.GetGenericTypeDefinition();
                if (object.ReferenceEquals(genericType, typeof(Nullable<>)))
                {
                    return nullableType.GetGenericArguments()[0];
                }
            }
            return null;
        }
    }
}
