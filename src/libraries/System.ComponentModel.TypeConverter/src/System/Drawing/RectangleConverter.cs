// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace System.Drawing
{
    public class RectangleConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        {
            return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
        }

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        {
            return destinationType == typeof(InstanceDescriptor) || base.CanConvertTo(context, destinationType);
        }

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string strValue)
            {
                string text = strValue.Trim();
                if (text.Length == 0)
                {
                    return null;
                }

                // Parse 4 integer values.
                if (culture == null)
                {
                    culture = CultureInfo.CurrentCulture;
                }

                char sep = culture.TextInfo.ListSeparator[0];
                string[] tokens = text.Split(sep);
                int[] values = new int[tokens.Length];
                TypeConverter intConverter = TypeDescriptor.GetConverterTrimUnsafe(typeof(int));
                for (int i = 0; i < values.Length; i++)
                {
                    // Note: ConvertFromString will raise exception if value cannot be converted.
                    values[i] = (int)intConverter.ConvertFromString(context, culture, tokens[i])!;
                }

                if (values.Length != 4)
                {
                    throw new ArgumentException(SR.Format(SR.TextParseFailedFormat, text, "x, y, width, height"));
                }

                return new Rectangle(values[0], values[1], values[2], values[3]);
            }

            return base.ConvertFrom(context, culture, value);
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType!!)
        {
            if (value is Rectangle rect)
            {
                if (destinationType == typeof(string))
                {
                    if (culture == null)
                    {
                        culture = CultureInfo.CurrentCulture;
                    }

                    string sep = culture.TextInfo.ListSeparator + " ";
                    TypeConverter intConverter = TypeDescriptor.GetConverterTrimUnsafe(typeof(int));

                    // Note: ConvertToString will raise exception if value cannot be converted.
                    var args = new string?[]
                    {
                        intConverter.ConvertToString(context, culture, rect.X),
                        intConverter.ConvertToString(context, culture, rect.Y),
                        intConverter.ConvertToString(context, culture, rect.Width),
                        intConverter.ConvertToString(context, culture, rect.Height)
                    };
                    return string.Join(sep, args);
                }
                else if (destinationType == typeof(InstanceDescriptor))
                {
                    ConstructorInfo? ctor = typeof(Rectangle).GetConstructor(new Type[] {
                        typeof(int), typeof(int), typeof(int), typeof(int)});

                    if (ctor != null)
                    {
                        return new InstanceDescriptor(ctor, new object[] {
                            rect.X, rect.Y, rect.Width, rect.Height});
                    }
                }
            }

            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override object CreateInstance(ITypeDescriptorContext? context, IDictionary propertyValues!!)
        {
            object? x = propertyValues["X"];
            object? y = propertyValues["Y"];
            object? width = propertyValues["Width"];
            object? height = propertyValues["Height"];

            if (x == null || y == null || width == null || height == null ||
                !(x is int) || !(y is int) || !(width is int) || !(height is int))
            {
                throw new ArgumentException(SR.PropertyValueInvalidEntry);
            }

            return new Rectangle((int)x, (int)y, (int)width, (int)height);
        }

        public override bool GetCreateInstanceSupported(ITypeDescriptorContext? context) => true;

        private static readonly string[] s_propertySort = { "X", "Y", "Width", "Height" };

        [RequiresUnreferencedCode("The Type of value cannot be statically discovered. " + AttributeCollection.FilterRequiresUnreferencedCodeMessage)]
        public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext? context, object? value, Attribute[]? attributes)
        {
            PropertyDescriptorCollection props = TypeDescriptor.GetProperties(typeof(Rectangle), attributes);
            return props.Sort(s_propertySort);
        }

        public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;
    }
}
