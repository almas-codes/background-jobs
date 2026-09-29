using System.Linq.Expressions;
using System.Text.Json;
using BackgroundJobs.Abstractions;

namespace BackgroundJobs.Core;

/// <summary>
/// Turns a strongly-typed lambda like <c>x => x.SendAsync(userId, ct)</c>
/// into serializable invocation data — the secret sauce that makes the API work.
/// </summary>
public static class CallExpressionSerializer
{
    public static JobInvocationData Serialize<T>(Expression<Func<T, Task>> methodCall)
    {
        if (methodCall.Body is not MethodCallExpression call)
            throw new ArgumentException("Expression must be a single method call.", nameof(methodCall));

        var method     = call.Method;
        var parameters = method.GetParameters();

        var args = new object?[call.Arguments.Count];
        for (var i = 0; i < call.Arguments.Count; i++)
        {
            // CancellationToken is always supplied fresh at execution time by the worker.
            args[i] = parameters[i].ParameterType == typeof(CancellationToken)
                ? null
                : Evaluate(call.Arguments[i]);
        }

        return new JobInvocationData(
            typeof(T).AssemblyQualifiedName!,
            method.Name,
            parameters.Select(p => p.ParameterType.AssemblyQualifiedName!).ToArray(),
            JsonSerializer.Serialize(args));
    }

    private static object? Evaluate(Expression expression)
    {
        if (expression is ConstantExpression constant) return constant.Value;
        var lambda = Expression.Lambda(Expression.Convert(expression, typeof(object)));
        return lambda.Compile().DynamicInvoke();
    }
}
