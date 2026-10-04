// ToolBelt.Wpf satellite test entry point. Discovers and runs every *Tests class in this assembly.
using System.Reflection;
using ToolBelt.Tests.Framework;

return TestRunner.Run(Assembly.GetExecutingAssembly(), args);
