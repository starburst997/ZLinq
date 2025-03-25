﻿using ConsoleAppFramework;
using System.Reflection;
using System.Text;
using ZLinq;

namespace FileGen;

public class DropInGen
{
    [Flags]
    enum DropInGenerateTypes
    {
        None = 0,
        Array = 1,
        Span = 2, // Span + ReadOnlySpan
        Memory = 4, // Memory + ReadOnlyMemory
        List = 8,
        Enumerable = 16,
        Collection = Array | Span | Memory | List,
        Everything = Array | Span | Memory | List | Enumerable
    }

    record struct DropInType(string Name, string Replacement, bool IsArray = false);

    [Command("dropin")]
    public void GenerateDropInSource()
    {
        var dropinTypes = new DropInType[]
        {
            new("Array", "FromArray", IsArray:true),
            new("Span", "FromSpan"),
            new("ReadOnlySpan", "FromSpan"),
            new("Memory", "FromMemory"),
            new("ReadOnlyMemory", "FromMemory"),
            new("List", "FromList"),
            new("IEnumerable", "FromEnumerable"),
        };

        foreach (var dropinType in dropinTypes)
        {
            var sb = new StringBuilder();
            if (dropinType.Replacement == "FromSpan")
            {
                sb.AppendLine("#if NET9_0_OR_GREATER");
            }

            sb.AppendLine("""
// <auto-generated />
#pragma warning disable
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
#if NET8_0_OR_GREATER
using System.Numerics;
#endif
using ZLinq;
using ZLinq.Linq;

internal static partial class ZLinqDropInExtensions
{
""");

            var methodInfos = typeof(ZLinq.ValueEnumerableExtensions).GetMethods();
            foreach (var methodInfo in methodInfos)
            {
                var signature = BuildSignature(methodInfo, dropinType);
                if (signature != null)
                {
                    sb.AppendLine(signature);
                }
            }
            sb.AppendLine(BuildCastAndOfTypeSignature(dropinType));

            sb.AppendLine("}");
            if (dropinType.Replacement == "FromSpan")
            {
                sb.AppendLine("#endif");
            }

            var code = sb.ToString();

            Console.WriteLine("Generate: " + dropinType.Name);
            File.WriteAllText("DropIn/" + dropinType.Name + ".cs", code);
        }
    }

    string? BuildSignature(MethodInfo methodInfo, DropInType dropInType)
    {
        if (methodInfo.Name is "GetType" or "ToString" or "Equals" or "GetHashCode" or "GetEnumerator")
        {
            return null;
        }

        if (methodInfo.Name.StartsWith("ThenBy"))
        {
            return null;
        }

        if (methodInfo.Name is "Where" && methodInfo.ReturnType.GetGenericArguments().Any(x => x.Name.Contains("SelectWhere")))
        {
            return null;
        }

        if (methodInfo.Name is "Select" && methodInfo.ReturnType.GetGenericArguments().Any(x => x.Name.Contains("WhereSelect")))
        {
            return null;
        }

        if (methodInfo.Name is "Contains" && !methodInfo.GetGenericArguments().Any(x => x.Name == "TEnumerator"))
        {
            return null;
        }

        // debugging stop condition
        // if (methodInfo.Name is not "Select") continue;

        var returnType = BuildType(methodInfo, methodInfo.ReturnType, dropInType.Replacement) + IsNullableReturnParameter(methodInfo);
        var name = methodInfo.Name;
        var genericsTypes = string.Join(", ", methodInfo.GetGenericArguments().Skip(1).Select(x => x.Name).ToArray());
        var parameters = string.Join(", ", methodInfo.GetParameters().Skip(1).Select(x => $"{BuildParameterType(methodInfo, x, dropInType.Replacement)} {x.Name}").ToArray());
        if (parameters != "") parameters = $", {parameters}";
        var parameterNames = string.Join(", ", methodInfo.GetParameters().Skip(1).Select(x => BuildParameterName(x)).ToArray());
        var sourceType = BuildSourceType(methodInfo, dropInType.Name, dropInType.IsArray);
        var constraints = BuildConstraints(methodInfo);

        var source = "source";
        if (dropInType.Name is "Array" or "List" or "IEnumerable")
        {
            source = "(source ?? throw new ArgumentNullException(\"source\"))";
        }

        var signature = $"    public static {returnType} {name}<{genericsTypes}>(this {sourceType} source{parameters}){constraints} => {source}.AsValueEnumerable().{name}({parameterNames});";

        // quick fix
        if (signature.Contains("RightJoin"))
        {
            signature = signature.Replace("Func<TOuter, TInner, TResult> resultSelector", "Func<TOuter?, TInner, TResult> resultSelector");
        }
        else if (signature.Contains("LeftJoin"))
        {
            signature = signature.Replace("Func<TOuter, TInner, TResult> resultSelector", "Func<TOuter, TInner?, TResult> resultSelector");
        }
        // Index and Range can support.
        //        else if ((methodInfo.Name.Contains("ElementAt") && signature.Contains("Index")) || (methodInfo.Name == "Take" && signature.Contains("Range")))
        //        {
        //            signature = $$"""
        //#if NETSTANDARD2_1 || NET5_0_OR_GREATER
        //{{signature}}
        //#endif
        //""";
        //        }
        else if (methodInfo.Name == "SumUnchecked")
        {
            signature = $$"""
#if NET8_0_OR_GREATER
{{signature}}
#endif
""";
        }

        return signature;
    }

    string BuildCastAndOfTypeSignature(DropInType dropInType)
    {
        // for non-generic IEnumerable only
        if (dropInType.Name != "IEnumerable") return "";

        var enumeratorType = $"{dropInType.Replacement}<object>";

        return $$"""
    public static ValueEnumerable<Cast<{{enumeratorType}}, object, TResult>, TResult> Cast<TResult>(this System.Collections.IEnumerable source) => System.Linq.Enumerable.Cast<object>(source).AsValueEnumerable().Cast<TResult>();
    public static ValueEnumerable<OfType<{{enumeratorType}}, object, TResult>, TResult> OfType<TResult>(this System.Collections.IEnumerable source) => System.Linq.Enumerable.Cast<object>(source).AsValueEnumerable().OfType<TResult>();
""";
    }

    string IsNullableReturnParameter(MethodInfo methodInfo)
    {
        if (methodInfo.Name.EndsWith("OrDefault"))
        {
            // OrDefault and non defaultValue is nullable
            if (!methodInfo.GetParameters().Any(x => x.Name == "defaultValue"))
            {
                return "?";
            }
        }
        if (methodInfo.Name is "Max" or "MaxBy" or "Min" or "MinBy")
        {
            return "?";
        }

        return "";
    }

    string BuildParameterType(MethodInfo methodInfo, ParameterInfo param, string replacement)
    {
        bool isOut = param.IsOut;
        bool isIn = param.GetCustomAttributes(typeof(System.Runtime.InteropServices.InAttribute), false).Length > 0;
        bool isByRef = param.ParameterType.IsByRef && !param.IsOut;

        var type = (isOut || isIn || isByRef)
            ? BuildType(methodInfo, param.ParameterType.GetElementType()!, replacement)
            : BuildType(methodInfo, param.ParameterType, replacement);

        if (isOut) return "out " + type;
        if (isIn) return "in " + type;
        if (isByRef) return "ref " + type;
        return type;
    }

    string BuildParameterName(ParameterInfo param)
    {
        bool isOut = param.IsOut;
        bool isIn = param.GetCustomAttributes(typeof(System.Runtime.InteropServices.InAttribute), false).Length > 0;
        bool isByRef = param.ParameterType.IsByRef && !param.IsOut;

        if (isOut) return "out " + param.Name;
        if (isIn) return "in " + param.Name;
        if (isByRef) return "ref " + param.Name;
        return param.Name!;
    }

    string BuildType(MethodInfo methodInfo, Type type, string replacement)
    {
        var sourceGenericTypeName = methodInfo.GetGenericArguments().First(x => !x.Name.Contains("Enumerator")).Name;
        replacement = $"{replacement}<{sourceGenericTypeName}>";

        var sb = new StringBuilder();
        BuildTypeCore(sb, type, replacement);
        return sb.ToString();
    }

    void BuildTypeCore(StringBuilder builder, Type type, string replacement)
    {
        if (!type.IsGenericType)
        {
            if (type.Name is "TEnumerator" or "TEnumerator1")
            {
                builder.Append(replacement);
            }
            else if (type.Name == "Void")
            {
                builder.Append("void");
            }
            else
            {
                builder.Append(type.Name);
            }
            return;
        }

        builder.Append(type.Name, 0, type.Name.Length - 2); // `9 generic types
        builder.Append("<");

        var isFirst = true;
        foreach (var item in type.GenericTypeArguments)
        {
            if (isFirst)
            {
                isFirst = false;
            }
            else
            {
                builder.Append(", ");
            }

            BuildTypeCore(builder, item, replacement);
        }
        builder.Append(">");
    }

    string BuildSourceType(MethodInfo methodInfo, string replacement, bool isArray)
    {
        var sourceGenericTypeName = methodInfo.GetGenericArguments().First(x => !x.Name.Contains("Enumerator")).Name;

        if (methodInfo.Name is "Average" && methodInfo.ReturnType.Name.StartsWith("Nullable"))
        {
            sourceGenericTypeName = "Nullable<" + sourceGenericTypeName + ">";
        }
        else if (methodInfo.Name is "Sum" && methodInfo.ToString()!.Contains("Nullable"))
        {
            sourceGenericTypeName = "Nullable<" + sourceGenericTypeName + ">";
        }
        else if (methodInfo.Name is "ToDictionary" && methodInfo.ToString()!.Contains("KeyValuePair"))
        {
            sourceGenericTypeName = "KeyValuePair<TKey, TValue>";
        }

        if (isArray)
        {
            return sourceGenericTypeName + "[]";
        }
        return $"{replacement}<{sourceGenericTypeName}>";
    }

    string BuildConstraints(MethodInfo methodInfo)
    {
        if (methodInfo.Name is "ToDictionary")
        {
            return " where TKey : notnull";
        }

        if (methodInfo.Name is "Average" or "Sum" or "SumUnchecked")
        {
            if (methodInfo.GetParameters().Length == 2) // func
            {
                return """

        where TResult : struct
#if NET8_0_OR_GREATER
        , INumber<TResult>
#endif

""";
            }
            else
            {
                return """

        where TSource : struct
#if NET8_0_OR_GREATER
        , INumber<TSource>
#endif

""";
            }
        }

        if (methodInfo.Name is "Concat" or "Except" or "Intersect" or "Union" or "UnionBy" or "SequenceEqual")
        {
            if (!methodInfo.GetGenericArguments().Any(x => x.Name == "TEnumerator2"))
            {
                return "";
            }

            return """

        where TEnumerator2 : struct, IValueEnumerator<TSource>
#if NET9_0_OR_GREATER
        , allows ref struct
#endif

""";
        }

        if (methodInfo.Name is "ExceptBy" or "IntersectBy")
        {
            if (!methodInfo.GetGenericArguments().Any(x => x.Name == "TEnumerator2"))
            {
                return "";
            }

            return """

        where TEnumerator2 : struct, IValueEnumerator<TKey>
#if NET9_0_OR_GREATER
        , allows ref struct
#endif

""";
        }

        if (methodInfo.Name is "GroupJoin" or "Join" or "LeftJoin" or "RightJoin")
        {
            if (!methodInfo.GetGenericArguments().Any(x => x.Name == "TEnumerator2"))
            {
                return "";
            }

            return """

        where TEnumerator2 : struct, IValueEnumerator<TInner>
#if NET9_0_OR_GREATER
        , allows ref struct
#endif

""";
        }

        if (methodInfo.Name is "SelectMany")
        {
            if (!methodInfo.GetGenericArguments().Any(x => x.Name == "TEnumerator2"))
            {
                return "";
            }

            if (methodInfo.GetGenericArguments().Any(x => x.Name == "TCollection"))
            {
                return """

        where TEnumerator2 : struct, IValueEnumerator<TCollection>
#if NET9_0_OR_GREATER
        , allows ref struct
#endif

""";
            }
            else
            {
                return """

        where TEnumerator2 : struct, IValueEnumerator<TResult>
#if NET9_0_OR_GREATER
        , allows ref struct
#endif

""";
            }
        }

        if (methodInfo.Name is "Zip")
        {
            if (!methodInfo.GetGenericArguments().Any(x => x.Name == "TEnumerator2"))
            {
                return "";
            }

            if (methodInfo.GetGenericArguments().Any(x => x.Name == "TThird"))
            {
                return """

        where TEnumerator2 : struct, IValueEnumerator<TSecond>
#if NET9_0_OR_GREATER
        , allows ref struct
#endif
        where TEnumerator3 : struct, IValueEnumerator<TThird>
#if NET9_0_OR_GREATER
        , allows ref struct
#endif

""";
            }
            else
            {
                return """

        where TEnumerator2 : struct, IValueEnumerator<TSecond>
#if NET9_0_OR_GREATER
        , allows ref struct
#endif

""";
            }
        }


        return "";
    }
}
