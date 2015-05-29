using System.Collections.Generic;
using System.Threading;

namespace RCRunner
{
    public class TestCasesController
    {
        private List<TestMethod> _testCasesList;

        public event TestRunFinishedDelegate Finished;

        public event TestRunFinishedDelegate MethodStatusChanged;

        private ITestFrameworkRunner _testFrameworkRunner;

        public event CheckCanceled Canceled;

        private int _totRunningScripts;

        private readonly PluginLoader _pluginLoader;

        protected virtual bool OnCanceled()
        {
            var handler = Canceled;
            return handler != null && handler();
        }

        public void SetTestRunner(ITestFrameworkRunner testFrameworkRunner)
        {
            _testFrameworkRunner = testFrameworkRunner;
        }

        protected virtual void OnMethodStatusChanged(TestMethod testcasemethod)
        {
            var handler = MethodStatusChanged;
            if (handler != null) handler(testcasemethod);
        }

        protected virtual void OnFinished(TestMethod testcasemethod)
        {
            var handler = Finished;
            if (handler != null) handler(testcasemethod);
        }

        private void OnTaskTestRunFinishedEvent(TestMethod testcaseMethod)
        {
            _totRunningScripts--;
            OnFinished(testcaseMethod);
        }

        public TestCasesController()
        {
            _pluginLoader = new PluginLoader();
            _pluginLoader.LoadTestExecutionPlugins();
        }

        private void DoWorkCore()
        {
            _totRunningScripts = 0;

            foreach (var testClass in _testCasesList)
            {
                testClass.TestExecutionStatus = TestExecutionStatus.Waiting;
                testClass.LastExecutionErrorMsg = string.Empty;
                OnMethodStatusChanged(testClass);
            }

            foreach (var testMethod in _testCasesList)
            {
                while (_totRunningScripts >= Properties.Settings.Default.MaxThreads)
                {
                    if (OnCanceled()) return;
                }

                _totRunningScripts++;
                if (OnCanceled()) return;
                testMethod.TestExecutionStatus = TestExecutionStatus.Running;
                OnMethodStatusChanged(testMethod);
                var task = new TestCaseRunner(testMethod, _testFrameworkRunner, _pluginLoader);
                task.TestRunFinished += OnTaskTestRunFinishedEvent;
                task.DoWork();
            }
        }

        public void DoWork(List<TestMethod> testCasesList)
        {
            _testCasesList = testCasesList;
            var t = new Thread(DoWorkCore);
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }
    }
}