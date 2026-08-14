using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NameGen.Tests
{
    /// <summary>
    /// Dependency-free test harness (runs via `dotnet run`, exit code = failure count).
    /// xUnit-style asserts so migrating to xUnit later is mechanical: add the package,
    /// swap [Test] for [Fact], delete this file.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute { }

    public static class Assert
    {
        public sealed class AssertFailedException : Exception
        {
            public AssertFailedException(string message) : base(message) { }
        }

        public static void True(bool condition, string? message = null)
        {
            if (!condition) throw new AssertFailedException(message ?? "Expected true.");
        }

        public static void False(bool condition, string? message = null)
        {
            if (condition) throw new AssertFailedException(message ?? "Expected false.");
        }

        public static void Equal<T>(T expected, T actual, string? message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AssertFailedException(message ?? $"Expected: {expected}\nActual:   {actual}");
        }

        public static void NotEqual<T>(T notExpected, T actual, string? message = null)
        {
            if (EqualityComparer<T>.Default.Equals(notExpected, actual))
                throw new AssertFailedException(message ?? $"Did not expect: {notExpected}");
        }

        public static void InRange(double value, double lo, double hi, string? message = null)
        {
            if (value < lo || value > hi)
                throw new AssertFailedException(message ?? $"Expected {value} in [{lo}, {hi}].");
        }

        public static T Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T e) { return e; }
            catch (Exception e) { throw new AssertFailedException($"Expected {typeof(T).Name}, got {e.GetType().Name}: {e.Message}"); }
            throw new AssertFailedException($"Expected {typeof(T).Name}, nothing was thrown.");
        }
    }

    public static class Runner
    {
        public static int RunAll(Assembly assembly)
        {
            var methods = assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
                .OrderBy(m => m.DeclaringType!.Name).ThenBy(m => m.Name)
                .ToList();

            int passed = 0;
            var failures = new List<string>();

            foreach (var m in methods)
            {
                string id = $"{m.DeclaringType!.Name}.{m.Name}";
                try
                {
                    object? instance = m.IsStatic ? null : Activator.CreateInstance(m.DeclaringType!);
                    m.Invoke(instance, null);
                    passed++;
                    Console.WriteLine($"  ok    {id}");
                }
                catch (TargetInvocationException tie)
                {
                    var inner = tie.InnerException ?? tie;
                    failures.Add($"{id}: {inner.Message}");
                    Console.WriteLine($"  FAIL  {id}");
                    Console.WriteLine($"        {inner.Message.Replace("\n", "\n        ")}");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"{passed}/{methods.Count} passed, {failures.Count} failed.");
            return failures.Count;
        }
    }

    public static class Program
    {
        public static int Main() => Runner.RunAll(typeof(Program).Assembly);
    }
}
