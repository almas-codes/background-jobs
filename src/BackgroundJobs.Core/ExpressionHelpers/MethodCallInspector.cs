using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace BackgroundJobs.Core.ExpressionHelpers;

public static class MethodCallInspector
{
    public static (string TypeName, string MethodName, string SerializedArguments) Inspect<T>(Expression<Func<T, System.Threading.Tasks.Task>> expression)
    {
        if (expression.Body is not MethodCallExpression methodCall)
        {
            throw new ArgumentException("Expression body must be a method call.", nameof(expression));
        }

        var method = methodCall.Method;
        var type = method.DeclaringType ?? typeof(T);
        var typeName = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
        var methodName = method.Name;

        var arguments = methodCall.Arguments.Select(Evaluate).ToArray();
        var serializedArguments = JsonSerializer.Serialize(arguments);

        return (typeName, methodName, serializedArguments);
    }

    private static object? Evaluate(Expression expression)
    {
        if (expression is ConstantExpression constant)
        {
            return constant.Value;
        }
        
        var objectMember = Expression.Convert(expression, typeof(object));
        var getterLambda = Expression.Lambda<Func<object>>(objectMember);
        var getter = getterLambda.Compile();
        return getter();
    }
}
