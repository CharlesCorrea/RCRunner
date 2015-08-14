﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RCRunner.PluginsStruct;

namespace RCRunner
{
    public delegate bool CheckCanceled();

    public class RCRunnerAPI
    {
        public readonly List<TestScript> TestClassesList;

        private readonly TestScriptsController _testCasesController;

        public TestRunFinishedDelegate MethodStatusChanged;

        public List<string> CustomAttributesList;

        private PluginLoader _pluginLoader;

        public void OnMethodStatusChanged(TestScript testcaseScript)
        {
            RunningTestsCount.Update(testcaseScript);
            if (MethodStatusChanged != null) MethodStatusChanged(testcaseScript);

            if (Done())
            {
                _pluginLoader.CallAfterTestRunPlugins();
            }
        }

        private TestFrameworkRunner _testFrameworkRunner;

        private bool _canceled;

        public readonly RunningTestsCount RunningTestsCount;

        private bool CheckTasksCanceled()
        {
            return _canceled;
        }

        public void Cancel()
        {
            _canceled = true;
        }

        public RCRunnerAPI()
        {
            RunningTestsCount = new RunningTestsCount();
            _testCasesController = new TestScriptsController();
            TestClassesList = new List<TestScript>();
            CustomAttributesList = new List<string>();
            _testCasesController.TestCaseStatusChanged = OnMethodStatusChanged;
            _testCasesController.Canceled = CheckTasksCanceled;
            _canceled = false;
        }

        public void SetTestRunner(TestFrameworkRunner testFrameworkRunner)
        {
            _testFrameworkRunner = testFrameworkRunner;
            _testCasesController.SetTestRunner(testFrameworkRunner);
        }

        public void SetPluginLoader(PluginLoader pluginLoader)
        {
            _pluginLoader = pluginLoader;
        }

        private string GetDescriptionAttributeValue(MemberInfo methodInfo)
        {
            var descriptionAttributeName = _testFrameworkRunner.GetTestMethodDescriptionAttribute();

            var descriptionAttr = Attribute.GetCustomAttributes(methodInfo).FirstOrDefault(x => x.GetType().FullName == descriptionAttributeName);

            var description = string.Empty;

            if (descriptionAttr == null) return description;

            var descriptionProperty = descriptionAttr.GetType().GetProperty("Description");

            if (descriptionProperty != null)
            {
                description = descriptionProperty.GetValue(descriptionAttr, null) as string;
            }

            return description;
        }

        private IList<MethodInfo> GetTestMethodsList(Type classObject)
        {
            var testAttributeName = _testFrameworkRunner.GetTestMethodAttribute();

            var rawMethods = classObject.GetMethods().Where(x => x.GetCustomAttributes().Any(y => y.GetType().FullName == testAttributeName));

            var methodInfos = rawMethods as IList<MethodInfo> ?? rawMethods.ToList();

            return methodInfos;
        }

        private List<string> GetCustomAttributes(MethodInfo method)
        {
            if (method == null) return null;

            var testAttributeName = _testFrameworkRunner.GetTestMethodAttribute();

            var descriptionAttributeName = _testFrameworkRunner.GetTestMethodDescriptionAttribute();

            var attributesList = method.CustomAttributes.Where(x => x.AttributeType.FullName != testAttributeName && x.AttributeType.FullName != descriptionAttributeName).Select(x => x.AttributeType.Name.Replace("Attribute", "")).ToList();

            var tempList = attributesList.Except(CustomAttributesList).ToList();

            CustomAttributesList.AddRange(tempList);

            return attributesList;
        }

        public void LoadAssembly()
        {
            var assembly = Assembly.LoadFrom(_testFrameworkRunner.GetAssemblyPath());

            TestClassesList.Clear();

            CustomAttributesList.Clear();

            foreach (var classes in assembly.GetTypes())
            {
                if (!classes.IsClass && !classes.IsPublic) continue;

                var methodInfos = GetTestMethodsList(classes);

                if (!methodInfos.Any()) continue;

                var className = classes.Name;

                foreach (var testMethod in methodInfos)
                {
                    var testScript = new TestScript
                    {
                        ClassName = className,
                        Name = testMethod.Name,
                        TestExecutionStatus = TestExecutionStatus.Active,
                        LastExecutionErrorMsg = string.Empty,
                        TestDescription = GetDescriptionAttributeValue(testMethod),
                        CustomAtributteList = GetCustomAttributes(testMethod)
                    };

                    TestClassesList.Add(testScript);
                }
            }
        }

        public void RunTestCases(List<TestScript> testCasesList)
        {
            RunningTestsCount.Reset();
            _canceled = false;

            _pluginLoader.CallBeforeTestRunPlugins();

            foreach (var testMethod in testCasesList)
            {
                testMethod.TestExecutionStatus = TestExecutionStatus.Waiting;
                testMethod.LastExecutionErrorMsg = string.Empty;
                OnMethodStatusChanged(testMethod);
            }
            _testCasesController.DoWork(testCasesList);
        }

        public bool Done()
        {
            return RunningTestsCount.Done();
        }

    }
}
