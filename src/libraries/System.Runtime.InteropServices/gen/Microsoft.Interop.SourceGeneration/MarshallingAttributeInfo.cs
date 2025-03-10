// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Microsoft.Interop
{

    /// <summary>
    /// Type used to pass on default marshalling details.
    /// </summary>
    public sealed record DefaultMarshallingInfo(
        CharEncoding CharEncoding
    );

    // The following types are modeled to fit with the current prospective spec
    // for C# vNext discriminated unions. Once discriminated unions are released,
    // these should be updated to be implemented as a discriminated union.

    public abstract record MarshallingInfo
    {
        // Add a constructor that can only be called by derived types in the same assembly
        // to enforce that this type cannot be extended by users of this library.
        private protected MarshallingInfo()
        { }
    }

    /// <summary>
    /// No marshalling information exists for the type.
    /// </summary>
    public sealed record NoMarshallingInfo : MarshallingInfo
    {
        public static readonly MarshallingInfo Instance = new NoMarshallingInfo();

        private NoMarshallingInfo() { }
    }

    /// <summary>
    /// Marshalling information is lacking because of support not because it is
    /// unknown or non-existent.
    /// </summary>
    /// <remarks>
    /// An indication of "missing support" will trigger the fallback logic, which is
    /// the forwarder marshaler.
    /// </remarks>
    public record MissingSupportMarshallingInfo : MarshallingInfo;

    /// <summary>
    /// Character encoding enumeration.
    /// </summary>
    public enum CharEncoding
    {
        Undefined,
        Utf8,
        Utf16,
        Ansi,
        PlatformDefined
    }

    /// <summary>
    /// Details that are required when scenario supports strings.
    /// </summary>
    public record MarshallingInfoStringSupport(
        CharEncoding CharEncoding
    ) : MarshallingInfo;

    /// <summary>
    /// Simple User-application of System.Runtime.InteropServices.MarshalAsAttribute
    /// </summary>
    public sealed record MarshalAsInfo(
        UnmanagedType UnmanagedType,
        CharEncoding CharEncoding) : MarshallingInfoStringSupport(CharEncoding)
    {
    }

    /// <summary>
    /// The provided type was determined to be an "unmanaged" type that can be passed as-is to native code.
    /// </summary>
    public sealed record UnmanagedBlittableMarshallingInfo : MarshallingInfo;

    [Flags]
    public enum CustomMarshallingFeatures
    {
        None = 0,
        ManagedToNative = 0x1,
        NativeToManaged = 0x2,
        ManagedToNativeStackalloc = 0x4,
        ManagedTypePinning = 0x8,
        NativeTypePinning = 0x10,
        FreeNativeResources = 0x20,
    }

    public abstract record CountInfo
    {
        private protected CountInfo() { }
    }

    public sealed record NoCountInfo : CountInfo
    {
        public static readonly NoCountInfo Instance = new NoCountInfo();

        private NoCountInfo() { }
    }

    public sealed record ConstSizeCountInfo(int Size) : CountInfo;

    public sealed record CountElementCountInfo(TypePositionInfo ElementInfo) : CountInfo
    {
        public const string ReturnValueElementName = "return-value";
    }

    public sealed record SizeAndParamIndexInfo(int ConstSize, TypePositionInfo? ParamAtIndex) : CountInfo
    {
        public const int UnspecifiedConstSize = -1;

        public const TypePositionInfo UnspecifiedParam = null;

        public static readonly SizeAndParamIndexInfo Unspecified = new(UnspecifiedConstSize, UnspecifiedParam);
    }

    /// <summary>
    /// User-applied System.Runtime.InteropServices.NativeMarshallingAttribute
    /// </summary>
    public record NativeMarshallingAttributeInfo(
        ManagedTypeInfo NativeMarshallingType,
        ManagedTypeInfo? ValuePropertyType,
        CustomMarshallingFeatures MarshallingFeatures,
        bool UseDefaultMarshalling) : MarshallingInfo;

    /// <summary>
    /// User-applied System.Runtime.InteropServices.GeneratedMarshallingAttribute
    /// on a non-blittable type in source in this compilation.
    /// </summary>
    public sealed record GeneratedNativeMarshallingAttributeInfo(
        string NativeMarshallingFullyQualifiedTypeName) : MarshallingInfo;

    /// <summary>
    /// The type of the element is a SafeHandle-derived type with no marshalling attributes.
    /// </summary>
    public sealed record SafeHandleMarshallingInfo(bool AccessibleDefaultConstructor, bool IsAbstract) : MarshallingInfo;

    /// <summary>
    /// User-applied System.Runtime.InteropServices.NativeMarshallingAttribute
    /// with a contiguous collection marshaller
    /// </summary>
    public sealed record NativeContiguousCollectionMarshallingInfo(
        ManagedTypeInfo NativeMarshallingType,
        ManagedTypeInfo? ValuePropertyType,
        CustomMarshallingFeatures MarshallingFeatures,
        bool UseDefaultMarshalling,
        CountInfo ElementCountInfo,
        ManagedTypeInfo ElementType,
        MarshallingInfo ElementMarshallingInfo) : NativeMarshallingAttributeInfo(
            NativeMarshallingType,
            ValuePropertyType,
            MarshallingFeatures,
            UseDefaultMarshalling
        );


    /// <summary>
    /// Marshalling information is lacking because of support not because it is
    /// unknown or non-existent. Includes information about element types in case
    /// we need to rehydrate the marshalling info into an attribute for the fallback marshaller.
    /// </summary>
    /// <remarks>
    /// An indication of "missing support" will trigger the fallback logic, which is
    /// the forwarder marshaler.
    /// </remarks>
    public sealed record MissingSupportCollectionMarshallingInfo(CountInfo CountInfo, MarshallingInfo ElementMarshallingInfo) : MissingSupportMarshallingInfo;

    public sealed class MarshallingAttributeInfoParser
    {
        private readonly Compilation _compilation;
        private readonly IGeneratorDiagnostics _diagnostics;
        private readonly DefaultMarshallingInfo _defaultInfo;
        private readonly ISymbol _contextSymbol;
        private readonly ITypeSymbol _marshalAsAttribute;
        private readonly ITypeSymbol _marshalUsingAttribute;

        public MarshallingAttributeInfoParser(
            Compilation compilation,
            IGeneratorDiagnostics diagnostics,
            DefaultMarshallingInfo defaultInfo,
            ISymbol contextSymbol)
        {
            _compilation = compilation;
            _diagnostics = diagnostics;
            _defaultInfo = defaultInfo;
            _contextSymbol = contextSymbol;
            _marshalAsAttribute = compilation.GetTypeByMetadataName(TypeNames.System_Runtime_InteropServices_MarshalAsAttribute)!;
            _marshalUsingAttribute = compilation.GetTypeByMetadataName(TypeNames.MarshalUsingAttribute)!;
        }

        public MarshallingInfo ParseMarshallingInfo(
            ITypeSymbol managedType,
            IEnumerable<AttributeData> useSiteAttributes)
        {
            return ParseMarshallingInfo(managedType, useSiteAttributes, ImmutableHashSet<string>.Empty);
        }

        private MarshallingInfo ParseMarshallingInfo(
            ITypeSymbol managedType,
            IEnumerable<AttributeData> useSiteAttributes,
            ImmutableHashSet<string> inspectedElements)
        {
            Dictionary<int, AttributeData> marshallingAttributesByIndirectionLevel = new();
            int maxIndirectionLevelDataProvided = 0;
            foreach (AttributeData attribute in useSiteAttributes)
            {
                if (TryGetAttributeIndirectionLevel(attribute, out int indirectionLevel))
                {
                    if (marshallingAttributesByIndirectionLevel.ContainsKey(indirectionLevel))
                    {
                        _diagnostics.ReportInvalidMarshallingAttributeInfo(attribute, nameof(Resources.DuplicateMarshallingInfo), indirectionLevel.ToString());
                        return NoMarshallingInfo.Instance;
                    }
                    marshallingAttributesByIndirectionLevel.Add(indirectionLevel, attribute);
                    maxIndirectionLevelDataProvided = Math.Max(maxIndirectionLevelDataProvided, indirectionLevel);
                }
            }

            int maxIndirectionLevelUsed = 0;
            MarshallingInfo info = GetMarshallingInfo(
                managedType,
                marshallingAttributesByIndirectionLevel,
                indirectionLevel: 0,
                inspectedElements,
                ref maxIndirectionLevelUsed);
            if (maxIndirectionLevelUsed < maxIndirectionLevelDataProvided)
            {
                _diagnostics.ReportInvalidMarshallingAttributeInfo(
                    marshallingAttributesByIndirectionLevel[maxIndirectionLevelDataProvided],
                    nameof(Resources.ExtraneousMarshallingInfo),
                    maxIndirectionLevelDataProvided.ToString(),
                    maxIndirectionLevelUsed.ToString());
            }
            return info;
        }

        private MarshallingInfo GetMarshallingInfo(
            ITypeSymbol type,
            Dictionary<int, AttributeData> useSiteAttributes,
            int indirectionLevel,
            ImmutableHashSet<string> inspectedElements,
            ref int maxIndirectionLevelUsed)
        {
            maxIndirectionLevelUsed = Math.Max(indirectionLevel, maxIndirectionLevelUsed);
            CountInfo parsedCountInfo = NoCountInfo.Instance;

            if (useSiteAttributes.TryGetValue(indirectionLevel, out AttributeData useSiteAttribute))
            {
                INamedTypeSymbol attributeClass = useSiteAttribute.AttributeClass!;

                if (indirectionLevel == 0
                    && SymbolEqualityComparer.Default.Equals(_compilation.GetTypeByMetadataName(TypeNames.System_Runtime_InteropServices_MarshalAsAttribute), attributeClass))
                {
                    // https://docs.microsoft.com/dotnet/api/system.runtime.interopservices.marshalasattribute
                    return CreateInfoFromMarshalAs(type, useSiteAttribute, inspectedElements, ref maxIndirectionLevelUsed);
                }
                else if (SymbolEqualityComparer.Default.Equals(_compilation.GetTypeByMetadataName(TypeNames.MarshalUsingAttribute), attributeClass))
                {
                    if (parsedCountInfo != NoCountInfo.Instance)
                    {
                        _diagnostics.ReportInvalidMarshallingAttributeInfo(useSiteAttribute, nameof(Resources.DuplicateCountInfo));
                        return NoMarshallingInfo.Instance;
                    }
                    parsedCountInfo = CreateCountInfo(useSiteAttribute, inspectedElements);
                    if (useSiteAttribute.ConstructorArguments.Length != 0)
                    {
                        return CreateNativeMarshallingInfo(
                            type,
                            useSiteAttribute,
                            isMarshalUsingAttribute: true,
                            indirectionLevel,
                            parsedCountInfo,
                            useSiteAttributes,
                            inspectedElements,
                            ref maxIndirectionLevelUsed);
                    }
                }
            }

            // If we aren't overriding the marshalling at usage time,
            // then fall back to the information on the element type itself.
            foreach (AttributeData typeAttribute in type.GetAttributes())
            {
                INamedTypeSymbol attributeClass = typeAttribute.AttributeClass!;

                if (attributeClass.ToDisplayString() == TypeNames.NativeMarshallingAttribute)
                {
                    return CreateNativeMarshallingInfo(
                        type,
                        typeAttribute,
                        isMarshalUsingAttribute: false,
                        indirectionLevel,
                        parsedCountInfo,
                        useSiteAttributes,
                        inspectedElements,
                        ref maxIndirectionLevelUsed);
                }
                else if (attributeClass.ToDisplayString() == TypeNames.GeneratedMarshallingAttribute)
                {
                    return type.IsConsideredBlittable() ? GetBlittableMarshallingInfo(type) : new GeneratedNativeMarshallingAttributeInfo(null! /* TODO: determine naming convention */);
                }
            }

            // If the type doesn't have custom attributes that dictate marshalling,
            // then consider the type itself.
            if (TryCreateTypeBasedMarshallingInfo(
                type,
                parsedCountInfo,
                indirectionLevel,
                useSiteAttributes,
                inspectedElements,
                ref maxIndirectionLevelUsed,
                out MarshallingInfo infoMaybe))
            {
                return infoMaybe;
            }

            return NoMarshallingInfo.Instance;
        }

        private CountInfo CreateCountInfo(AttributeData marshalUsingData, ImmutableHashSet<string> inspectedElements)
        {
            int? constSize = null;
            string? elementName = null;
            foreach (KeyValuePair<string, TypedConstant> arg in marshalUsingData.NamedArguments)
            {
                if (arg.Key == ManualTypeMarshallingHelper.MarshalUsingProperties.ConstantElementCount)
                {
                    constSize = (int)arg.Value.Value!;
                }
                else if (arg.Key == ManualTypeMarshallingHelper.MarshalUsingProperties.CountElementName)
                {
                    if (arg.Value.Value is null)
                    {
                        _diagnostics.ReportConfigurationNotSupported(marshalUsingData, ManualTypeMarshallingHelper.MarshalUsingProperties.CountElementName, "null");
                        return NoCountInfo.Instance;
                    }
                    elementName = (string)arg.Value.Value!;
                }
            }

            if (constSize is not null && elementName is not null)
            {
                _diagnostics.ReportInvalidMarshallingAttributeInfo(marshalUsingData, nameof(Resources.ConstantAndElementCountInfoDisallowed));
            }
            else if (constSize is not null)
            {
                return new ConstSizeCountInfo(constSize.Value);
            }
            else if (elementName is not null)
            {
                if (inspectedElements.Contains(elementName))
                {
                    throw new CyclicalCountElementInfoException(inspectedElements, elementName);
                }

                try
                {
                    TypePositionInfo? elementInfo = CreateForElementName(elementName, inspectedElements.Add(elementName));
                    if (elementInfo is null)
                    {
                        _diagnostics.ReportConfigurationNotSupported(marshalUsingData, ManualTypeMarshallingHelper.MarshalUsingProperties.CountElementName, elementName);
                        return NoCountInfo.Instance;
                    }
                    return new CountElementCountInfo(elementInfo);
                }
                // Specifically catch the exception when we're trying to inspect the element that started the cycle.
                // This ensures that we've unwound the whole cycle so when we return NoCountInfo.Instance, there will be no cycles in the count info.
                catch (CyclicalCountElementInfoException ex) when (ex.StartOfCycle == elementName)
                {
                    _diagnostics.ReportInvalidMarshallingAttributeInfo(marshalUsingData, nameof(Resources.CyclicalCountInfo), elementName);
                    return NoCountInfo.Instance;
                }
            }

            return NoCountInfo.Instance;
        }

        private TypePositionInfo? CreateForParamIndex(AttributeData attrData, int paramIndex, ImmutableHashSet<string> inspectedElements)
        {
            if (!(_contextSymbol is IMethodSymbol method && 0 <= paramIndex && paramIndex < method.Parameters.Length))
            {
                return null;
            }
            IParameterSymbol param = method.Parameters[paramIndex];

            if (inspectedElements.Contains(param.Name))
            {
                throw new CyclicalCountElementInfoException(inspectedElements, param.Name);
            }

            try
            {
                return TypePositionInfo.CreateForParameter(
                    param,
                    ParseMarshallingInfo(param.Type, param.GetAttributes(), inspectedElements.Add(param.Name)), _compilation) with
                { ManagedIndex = paramIndex };
            }
            // Specifically catch the exception when we're trying to inspect the element that started the cycle.
            // This ensures that we've unwound the whole cycle so when we return, there will be no cycles in the count info.
            catch (CyclicalCountElementInfoException ex) when (ex.StartOfCycle == param.Name)
            {
                _diagnostics.ReportInvalidMarshallingAttributeInfo(attrData, nameof(Resources.CyclicalCountInfo), param.Name);
                return SizeAndParamIndexInfo.UnspecifiedParam;
            }
        }

        private TypePositionInfo? CreateForElementName(string elementName, ImmutableHashSet<string> inspectedElements)
        {
            if (_contextSymbol is IMethodSymbol method)
            {
                if (elementName == CountElementCountInfo.ReturnValueElementName)
                {
                    return new TypePositionInfo(
                        ManagedTypeInfo.CreateTypeInfoForTypeSymbol(method.ReturnType),
                        ParseMarshallingInfo(method.ReturnType, method.GetReturnTypeAttributes(), inspectedElements)) with
                    {
                        ManagedIndex = TypePositionInfo.ReturnIndex
                    };
                }

                for (int i = 0; i < method.Parameters.Length; i++)
                {
                    IParameterSymbol param = method.Parameters[i];
                    if (param.Name == elementName)
                    {
                        return TypePositionInfo.CreateForParameter(param, ParseMarshallingInfo(param.Type, param.GetAttributes(), inspectedElements), _compilation) with { ManagedIndex = i };
                    }
                }
            }
            else if (_contextSymbol is INamedTypeSymbol _)
            {
                // TODO: Handle when we create a struct marshalling generator
                // Do we want to support CountElementName pointing to only fields, or properties as well?
                // If only fields, how do we handle properties with generated backing fields?
            }

            return null;
        }

        private MarshallingInfo CreateInfoFromMarshalAs(
            ITypeSymbol type,
            AttributeData attrData,
            ImmutableHashSet<string> inspectedElements,
            ref int maxIndirectionLevelUsed)
        {
            object unmanagedTypeObj = attrData.ConstructorArguments[0].Value!;
            UnmanagedType unmanagedType = unmanagedTypeObj is short unmanagedTypeAsShort
                ? (UnmanagedType)unmanagedTypeAsShort
                : (UnmanagedType)unmanagedTypeObj;
            if (!Enum.IsDefined(typeof(UnmanagedType), unmanagedType)
                || unmanagedType == UnmanagedType.CustomMarshaler
                || unmanagedType == UnmanagedType.SafeArray)
            {
                _diagnostics.ReportConfigurationNotSupported(attrData, nameof(UnmanagedType), unmanagedType.ToString());
            }
            bool isArrayType = unmanagedType == UnmanagedType.LPArray || unmanagedType == UnmanagedType.ByValArray;
            UnmanagedType elementUnmanagedType = (UnmanagedType)SizeAndParamIndexInfo.UnspecifiedConstSize;
            SizeAndParamIndexInfo arraySizeInfo = SizeAndParamIndexInfo.Unspecified;

            // All other data on attribute is defined as NamedArguments.
            foreach (KeyValuePair<string, TypedConstant> namedArg in attrData.NamedArguments)
            {
                switch (namedArg.Key)
                {
                    default:
                        Debug.Fail($"An unknown member was found on {nameof(MarshalAsAttribute)}");
                        continue;
                    case nameof(MarshalAsAttribute.SafeArraySubType):
                    case nameof(MarshalAsAttribute.SafeArrayUserDefinedSubType):
                    case nameof(MarshalAsAttribute.IidParameterIndex):
                    case nameof(MarshalAsAttribute.MarshalTypeRef):
                    case nameof(MarshalAsAttribute.MarshalType):
                    case nameof(MarshalAsAttribute.MarshalCookie):
                        _diagnostics.ReportConfigurationNotSupported(attrData, $"{attrData.AttributeClass!.Name}{Type.Delimiter}{namedArg.Key}");
                        break;
                    case nameof(MarshalAsAttribute.ArraySubType):
                        if (!isArrayType)
                        {
                            _diagnostics.ReportConfigurationNotSupported(attrData, $"{attrData.AttributeClass!.Name}{Type.Delimiter}{namedArg.Key}");
                        }
                        elementUnmanagedType = (UnmanagedType)namedArg.Value.Value!;
                        break;
                    case nameof(MarshalAsAttribute.SizeConst):
                        if (!isArrayType)
                        {
                            _diagnostics.ReportConfigurationNotSupported(attrData, $"{attrData.AttributeClass!.Name}{Type.Delimiter}{namedArg.Key}");
                        }
                        arraySizeInfo = arraySizeInfo with { ConstSize = (int)namedArg.Value.Value! };
                        break;
                    case nameof(MarshalAsAttribute.SizeParamIndex):
                        if (!isArrayType)
                        {
                            _diagnostics.ReportConfigurationNotSupported(attrData, $"{attrData.AttributeClass!.Name}{Type.Delimiter}{namedArg.Key}");
                        }
                        TypePositionInfo? paramIndexInfo = CreateForParamIndex(attrData, (short)namedArg.Value.Value!, inspectedElements);

                        if (paramIndexInfo is null)
                        {
                            _diagnostics.ReportConfigurationNotSupported(attrData, nameof(MarshalAsAttribute.SizeParamIndex), namedArg.Value.Value.ToString());
                        }
                        arraySizeInfo = arraySizeInfo with { ParamAtIndex = paramIndexInfo };
                        break;
                }
            }

            if (!isArrayType)
            {
                return new MarshalAsInfo(unmanagedType, _defaultInfo.CharEncoding);
            }

            if (type is not IArrayTypeSymbol { ElementType: ITypeSymbol elementType })
            {
                _diagnostics.ReportConfigurationNotSupported(attrData, nameof(UnmanagedType), unmanagedType.ToString());
                return NoMarshallingInfo.Instance;
            }

            MarshallingInfo elementMarshallingInfo = NoMarshallingInfo.Instance;
            if (elementUnmanagedType != (UnmanagedType)SizeAndParamIndexInfo.UnspecifiedConstSize)
            {
                elementMarshallingInfo = new MarshalAsInfo(elementUnmanagedType, _defaultInfo.CharEncoding);
            }
            else
            {
                maxIndirectionLevelUsed = 1;
                elementMarshallingInfo = GetMarshallingInfo(elementType, new Dictionary<int, AttributeData>(), 1, ImmutableHashSet<string>.Empty, ref maxIndirectionLevelUsed);
            }

            INamedTypeSymbol? arrayMarshaller;

            if (elementType is IPointerTypeSymbol { PointedAtType: ITypeSymbol pointedAt })
            {
                arrayMarshaller = _compilation.GetTypeByMetadataName(TypeNames.System_Runtime_InteropServices_GeneratedMarshalling_PtrArrayMarshaller_Metadata)?.Construct(pointedAt);
            }
            else
            {
                arrayMarshaller = _compilation.GetTypeByMetadataName(TypeNames.System_Runtime_InteropServices_GeneratedMarshalling_ArrayMarshaller_Metadata)?.Construct(elementType);
            }

            if (arrayMarshaller is null)
            {
                // If the array marshaler type is not available, then we cannot marshal arrays but indicate it is missing.
                return new MissingSupportCollectionMarshallingInfo(arraySizeInfo, elementMarshallingInfo);
            }

            ITypeSymbol? valuePropertyType = ManualTypeMarshallingHelper.FindValueProperty(arrayMarshaller)?.Type;

            return new NativeContiguousCollectionMarshallingInfo(
                NativeMarshallingType: ManagedTypeInfo.CreateTypeInfoForTypeSymbol(arrayMarshaller),
                ValuePropertyType: valuePropertyType is not null ? ManagedTypeInfo.CreateTypeInfoForTypeSymbol(valuePropertyType) : null,
                MarshallingFeatures: ~CustomMarshallingFeatures.ManagedTypePinning,
                UseDefaultMarshalling: true,
                ElementCountInfo: arraySizeInfo,
                ElementType: ManagedTypeInfo.CreateTypeInfoForTypeSymbol(elementType),
                ElementMarshallingInfo: elementMarshallingInfo);
        }

        private MarshallingInfo CreateNativeMarshallingInfo(
            ITypeSymbol type,
            AttributeData attrData,
            bool isMarshalUsingAttribute,
            int indirectionLevel,
            CountInfo parsedCountInfo,
            Dictionary<int, AttributeData> useSiteAttributes,
            ImmutableHashSet<string> inspectedElements,
            ref int maxIndirectionLevelUsed)
        {
            CustomMarshallingFeatures features = CustomMarshallingFeatures.None;

            if (!isMarshalUsingAttribute && ManualTypeMarshallingHelper.FindGetPinnableReference(type) is not null)
            {
                features |= CustomMarshallingFeatures.ManagedTypePinning;
            }

            ITypeSymbol spanOfByte = _compilation.GetTypeByMetadataName(TypeNames.System_Span_Metadata)!.Construct(_compilation.GetSpecialType(SpecialType.System_Byte));

            INamedTypeSymbol nativeType = (INamedTypeSymbol)attrData.ConstructorArguments[0].Value!;

            if (nativeType.IsUnboundGenericType)
            {
                if (isMarshalUsingAttribute)
                {
                    _diagnostics.ReportInvalidMarshallingAttributeInfo(attrData, nameof(Resources.NativeGenericTypeMustBeClosedOrMatchArityMessage), nativeType.ToDisplayString());
                    return NoMarshallingInfo.Instance;
                }
                else if (type is INamedTypeSymbol namedType)
                {
                    if (namedType.Arity != nativeType.Arity)
                    {
                        _diagnostics.ReportInvalidMarshallingAttributeInfo(attrData, nameof(Resources.NativeGenericTypeMustBeClosedOrMatchArityMessage), nativeType.ToDisplayString());
                        return NoMarshallingInfo.Instance;
                    }
                    else
                    {
                        nativeType = nativeType.ConstructedFrom.Construct(namedType.TypeArguments.ToArray());
                    }
                }
                else
                {
                    _diagnostics.ReportInvalidMarshallingAttributeInfo(attrData, nameof(Resources.NativeGenericTypeMustBeClosedOrMatchArityMessage), nativeType.ToDisplayString());
                    return NoMarshallingInfo.Instance;
                }
            }

            ITypeSymbol contiguousCollectionMarshalerAttribute = _compilation.GetTypeByMetadataName(TypeNames.GenericContiguousCollectionMarshallerAttribute)!;

            bool isContiguousCollectionMarshaller = nativeType.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, contiguousCollectionMarshalerAttribute));
            IPropertySymbol? valueProperty = ManualTypeMarshallingHelper.FindValueProperty(nativeType);

            ManualTypeMarshallingHelper.NativeTypeMarshallingVariant marshallingVariant = isContiguousCollectionMarshaller
                ? ManualTypeMarshallingHelper.NativeTypeMarshallingVariant.ContiguousCollection
                : ManualTypeMarshallingHelper.NativeTypeMarshallingVariant.Standard;

            bool hasInt32Constructor = false;
            foreach (IMethodSymbol ctor in nativeType.Constructors)
            {
                if (ManualTypeMarshallingHelper.IsManagedToNativeConstructor(ctor, type, marshallingVariant) && (valueProperty is null or { GetMethod: not null }))
                {
                    features |= CustomMarshallingFeatures.ManagedToNative;
                }
                else if (ManualTypeMarshallingHelper.IsCallerAllocatedSpanConstructor(ctor, type, spanOfByte, marshallingVariant)
                    && (valueProperty is null or { GetMethod: not null }))
                {
                    features |= CustomMarshallingFeatures.ManagedToNativeStackalloc;
                }
                else if (ctor.Parameters.Length == 1 && ctor.Parameters[0].Type.SpecialType == SpecialType.System_Int32)
                {
                    hasInt32Constructor = true;
                }
            }

            // The constructor that takes only the native element size is required for collection marshallers
            // in the native-to-managed scenario.
            if ((!isContiguousCollectionMarshaller
                    || (hasInt32Constructor && ManualTypeMarshallingHelper.HasSetUnmarshalledCollectionLengthMethod(nativeType)))
                && ManualTypeMarshallingHelper.HasToManagedMethod(nativeType, type)
                && (valueProperty is null or { SetMethod: not null }))
            {
                features |= CustomMarshallingFeatures.NativeToManaged;
            }

            if (features == CustomMarshallingFeatures.None)
            {
                _diagnostics.ReportInvalidMarshallingAttributeInfo(
                    attrData,
                    isContiguousCollectionMarshaller
                        ? nameof(Resources.CollectionNativeTypeMustHaveRequiredShapeMessage)
                        : nameof(Resources.NativeTypeMustHaveRequiredShapeMessage),
                    nativeType.ToDisplayString());
                return NoMarshallingInfo.Instance;
            }

            if (ManualTypeMarshallingHelper.HasFreeNativeMethod(nativeType))
            {
                features |= CustomMarshallingFeatures.FreeNativeResources;
            }

            if (ManualTypeMarshallingHelper.FindGetPinnableReference(nativeType) is not null)
            {
                features |= CustomMarshallingFeatures.NativeTypePinning;
            }

            if (isContiguousCollectionMarshaller)
            {
                if (!ManualTypeMarshallingHelper.HasNativeValueStorageProperty(nativeType, spanOfByte))
                {
                    _diagnostics.ReportInvalidMarshallingAttributeInfo(attrData, nameof(Resources.CollectionNativeTypeMustHaveRequiredShapeMessage), nativeType.ToDisplayString());
                    return NoMarshallingInfo.Instance;
                }

                if (!ManualTypeMarshallingHelper.TryGetElementTypeFromContiguousCollectionMarshaller(nativeType, out ITypeSymbol elementType))
                {
                    _diagnostics.ReportInvalidMarshallingAttributeInfo(attrData, nameof(Resources.CollectionNativeTypeMustHaveRequiredShapeMessage), nativeType.ToDisplayString());
                    return NoMarshallingInfo.Instance;
                }

                return new NativeContiguousCollectionMarshallingInfo(
                    ManagedTypeInfo.CreateTypeInfoForTypeSymbol(nativeType),
                    valueProperty is not null ? ManagedTypeInfo.CreateTypeInfoForTypeSymbol(valueProperty.Type) : null,
                    features,
                    UseDefaultMarshalling: !isMarshalUsingAttribute,
                    parsedCountInfo,
                    ManagedTypeInfo.CreateTypeInfoForTypeSymbol(elementType),
                    GetMarshallingInfo(elementType, useSiteAttributes, indirectionLevel + 1, inspectedElements, ref maxIndirectionLevelUsed));
            }

            return new NativeMarshallingAttributeInfo(
                ManagedTypeInfo.CreateTypeInfoForTypeSymbol(nativeType),
                valueProperty is not null ? ManagedTypeInfo.CreateTypeInfoForTypeSymbol(valueProperty.Type) : null,
                features,
                UseDefaultMarshalling: !isMarshalUsingAttribute);
        }

        private bool TryCreateTypeBasedMarshallingInfo(
            ITypeSymbol type,
            CountInfo parsedCountInfo,
            int indirectionLevel,
            Dictionary<int, AttributeData> useSiteAttributes,
            ImmutableHashSet<string> inspectedElements,
            ref int maxIndirectionLevelUsed,
            out MarshallingInfo marshallingInfo)
        {
            // Check for an implicit SafeHandle conversion.
            // The SafeHandle type might not be defined if we're using one of the test CoreLib implementations used for NativeAOT.
            ITypeSymbol? safeHandleType = _compilation.GetTypeByMetadataName(TypeNames.System_Runtime_InteropServices_SafeHandle);
            if (safeHandleType is not null)
            {
                CodeAnalysis.Operations.CommonConversion conversion = _compilation.ClassifyCommonConversion(type, safeHandleType);
                if (conversion.Exists
                    && conversion.IsImplicit
                    && (conversion.IsReference || conversion.IsIdentity))
                {
                    bool hasAccessibleDefaultConstructor = false;
                    if (type is INamedTypeSymbol named && !named.IsAbstract && named.InstanceConstructors.Length > 0)
                    {
                        foreach (IMethodSymbol ctor in named.InstanceConstructors)
                        {
                            if (ctor.Parameters.Length == 0)
                            {
                                hasAccessibleDefaultConstructor = _compilation.IsSymbolAccessibleWithin(ctor, _contextSymbol.ContainingType);
                                break;
                            }
                        }
                    }
                    marshallingInfo = new SafeHandleMarshallingInfo(hasAccessibleDefaultConstructor, type.IsAbstract);
                    return true;
                }
            }

            if (type is IArrayTypeSymbol { ElementType: ITypeSymbol elementType })
            {
                INamedTypeSymbol? arrayMarshaller;

                if (elementType is IPointerTypeSymbol { PointedAtType: ITypeSymbol pointedAt })
                {
                    arrayMarshaller = _compilation.GetTypeByMetadataName(TypeNames.System_Runtime_InteropServices_GeneratedMarshalling_PtrArrayMarshaller_Metadata)?.Construct(pointedAt);
                }
                else
                {
                    arrayMarshaller = _compilation.GetTypeByMetadataName(TypeNames.System_Runtime_InteropServices_GeneratedMarshalling_ArrayMarshaller_Metadata)?.Construct(elementType);
                }

                if (arrayMarshaller is null)
                {
                    // If the array marshaler type is not available, then we cannot marshal arrays but indicate it is missing.
                    marshallingInfo = new MissingSupportCollectionMarshallingInfo(parsedCountInfo, GetMarshallingInfo(elementType, useSiteAttributes, indirectionLevel + 1, inspectedElements, ref maxIndirectionLevelUsed));
                    return true;
                }

                ITypeSymbol? valuePropertyType = ManualTypeMarshallingHelper.FindValueProperty(arrayMarshaller)?.Type;

                marshallingInfo = new NativeContiguousCollectionMarshallingInfo(
                    NativeMarshallingType: ManagedTypeInfo.CreateTypeInfoForTypeSymbol(arrayMarshaller),
                    ValuePropertyType: valuePropertyType is not null ? ManagedTypeInfo.CreateTypeInfoForTypeSymbol(valuePropertyType) : null,
                    MarshallingFeatures: ~CustomMarshallingFeatures.ManagedTypePinning,
                    UseDefaultMarshalling: true,
                    ElementCountInfo: parsedCountInfo,
                    ElementType: ManagedTypeInfo.CreateTypeInfoForTypeSymbol(elementType),
                    ElementMarshallingInfo: GetMarshallingInfo(elementType, useSiteAttributes, indirectionLevel + 1, inspectedElements, ref maxIndirectionLevelUsed));
                return true;
            }

            // No marshalling info was computed, but a character encoding was provided.
            // If the type is a character or string then pass on these details.
            if (_defaultInfo.CharEncoding != CharEncoding.Undefined
                && (type.SpecialType == SpecialType.System_Char
                    || type.SpecialType == SpecialType.System_String))
            {
                marshallingInfo = new MarshallingInfoStringSupport(_defaultInfo.CharEncoding);
                return true;
            }

            if (type is INamedTypeSymbol { IsUnmanagedType: true } unmanagedType
                && unmanagedType.IsConsideredBlittable())
            {
                marshallingInfo = GetBlittableMarshallingInfo(type);
                return true;
            }

            marshallingInfo = NoMarshallingInfo.Instance;
            return false;
        }

        private MarshallingInfo GetBlittableMarshallingInfo(ITypeSymbol type)
        {
            if (type.TypeKind is TypeKind.Enum or TypeKind.Pointer or TypeKind.FunctionPointer
                || type.SpecialType.IsAlwaysBlittable()
                || type.SpecialType == SpecialType.System_Boolean)
            {
                // Treat primitive types and enums as having no marshalling info.
                // They are supported in configurations where runtime marshalling is enabled.
                return NoMarshallingInfo.Instance;
            }

            else if (_compilation.GetTypeByMetadataName(TypeNames.System_Runtime_CompilerServices_DisableRuntimeMarshallingAttribute) is null)
            {
                // If runtime marshalling cannot be disabled, then treat this as a "missing support" scenario so we can gracefully fall back to using the fowarder downlevel.
                return new MissingSupportMarshallingInfo();
            }
            else
            {
                return new UnmanagedBlittableMarshallingInfo();
            }
        }

        private bool TryGetAttributeIndirectionLevel(AttributeData attrData, out int indirectionLevel)
        {
            if (SymbolEqualityComparer.Default.Equals(attrData.AttributeClass, _marshalAsAttribute))
            {
                indirectionLevel = 0;
                return true;
            }

            if (!SymbolEqualityComparer.Default.Equals(attrData.AttributeClass, _marshalUsingAttribute))
            {
                indirectionLevel = 0;
                return false;
            }

            foreach (KeyValuePair<string, TypedConstant> arg in attrData.NamedArguments)
            {
                if (arg.Key == ManualTypeMarshallingHelper.MarshalUsingProperties.ElementIndirectionLevel)
                {
                    indirectionLevel = (int)arg.Value.Value!;
                    return true;
                }
            }
            indirectionLevel = 0;
            return true;
        }

        private class CyclicalCountElementInfoException : Exception
        {
            public CyclicalCountElementInfoException(ImmutableHashSet<string> elementsInCycle, string startOfCycle)
            {
                ElementsInCycle = elementsInCycle;
                StartOfCycle = startOfCycle;
            }

            public ImmutableHashSet<string> ElementsInCycle { get; }

            public string StartOfCycle { get; }
        }
    }
}
