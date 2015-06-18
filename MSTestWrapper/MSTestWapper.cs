﻿using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using RCRunner;

namespace MSTestWrapper
{
    /// <summary>
    /// A test wrapper that implements the ITestFrameworkRunner as an adapter for the MSTest test framework
    /// </summary>
    public class MSTestWrapper : ITestFrameworkRunner
    {
        private string _assemblyPath;
        private string _resultFilePath;

        /// <summary>
        /// Returns the assembly that contains the test cases to run
        /// </summary>
        /// <returns>Returns the assembly path</returns>
        public string GetAssemblyPath()
        {
            return _assemblyPath;
        }

        /// <summary>
        /// Sets the assembly that contains the test cases to run
        /// </summary>
        /// <param name="assemblyPath">The assembly that contains the test cases to run</param>
        public void SetAssemblyPath(string assemblyPath)
        {
            _assemblyPath = assemblyPath;
        }

        /// <summary>
        /// Retuns the folder which the tests results will be stored
        /// </summary>
        /// <returns>The folder which the tests results will be stored</returns>
        public string GetTestResultsFolder()
        {
            return _resultFilePath;
        }

        /// <summary>
        /// Sets the folder which the tests results will be stored
        /// </summary>
        /// <param name="folder">The folder which the tests results will be stored</param>
        public void SetTestResultsFolder(string folder)
        {
            _resultFilePath = folder;
        }

        /// <summary>
        /// Returns the name of the attribute that defines a test method
        /// </summary>
        /// <returns>The name of the attribute that defines a test method</returns>
        public string GetTestMethodAttribute()
        {
            return typeof(TestMethodAttribute).FullName;
        }

        /// <summary>
        /// Deletes a file wating in case that it is being used by other applications
        /// </summary>
        /// <param name="file"></param>
        private static void SafeDeleteFile(string file)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(2000);
                File.Delete(file);
            }
        }

        /// <summary>
        /// Check if the error message returned by the test case is a timeout error
        /// </summary>
        /// <param name="testCase">The test cases to run</param>
        /// <param name="errorMsg">The error message</param>
        /// <returns></returns>
        private bool InternalRunTest(string testCase, ref string errorMsg)
        {
            var resultFilePath = Path.Combine(_resultFilePath, testCase);
            Directory.CreateDirectory(resultFilePath);

            var resultFile = Path.Combine(resultFilePath, testCase + ".trx");

            if (File.Exists(resultFile))
            {
                resultFile = Path.Combine(resultFilePath, testCase + "(2)" + ".trx");
            }

            var msTestPath = Settings.Default.MSTestExeLocation;

            if (!File.Exists(msTestPath))
                throw new FileNotFoundException("MSTest app not found on the specified path", msTestPath);

            if (!File.Exists(_assemblyPath))
                throw new FileNotFoundException("Test Assembly not found on the specified path", _assemblyPath);

            var testContainer = "/testcontainer:" + "\"" + _assemblyPath + "\"";
            var testParam = "/test:" + testCase;

            var resultParam = "/resultsfile:" + "\"" + resultFile + "\"";

            SafeDeleteFile(resultFile);

            try
            {
                var p = new Process
                {
                    StartInfo =
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        FileName = msTestPath,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        Arguments = testContainer + " " + testParam + " " + resultParam
                    }
                };

                if (!p.Start())
                {
                    throw new Exception("Error starting the MSTest process", new Exception(p.StandardError.ReadToEnd()));
                }

                p.WaitForExit();

                var testResult = GetTestStatusFromTrxFile(resultFile, ref errorMsg);

                return testResult;
            }

            finally
            {
                CleanUpDirectories(resultFilePath);
            }
        }

        /// <summary>
        /// Check if the error message returned by the test case is a timeout error
        /// </summary>
        /// <param name="errorMsg">The error message</param>
        /// <returns></returns>
        private static bool IsTimeOutError(string errorMsg)
        {
            return errorMsg.ToLower().Contains("timed out after");
        }

        /// <summary>
        /// Executes a test case specified by the testcase param
        /// </summary>
        /// <param name="testCase">The test case to run</param>
        public void RunTest(string testCase)
        {
            var errorMsg = string.Empty;

            var testResult = InternalRunTest(testCase, ref errorMsg);

            if (testResult) return;

            if (!IsTimeOutError(errorMsg)) throw new Exception(errorMsg);

            testResult = InternalRunTest(testCase, ref errorMsg);

            if (!testResult) throw new Exception(errorMsg);
        }

        /// <summary>
        /// Gets the result outcome and, in case of a failed test case, returns the test case execution error
        /// </summary>
        /// <param name="fileName">TRX file to read from</param>
        /// <param name="errorMsg">The error message pointer to return the error when the test fails</param>
        /// <returns>Returns true if the test ran successfuly or false if the test failed</returns>
        static bool GetTestStatusFromTrxFile(string fileName, ref string errorMsg)
        {
            var fileStreamReader = new StreamReader(fileName);
            var xmlSer = new XmlSerializer(typeof(TestRunType));
            var testRunType = (TestRunType)xmlSer.Deserialize(fileStreamReader);

            var resultType = testRunType.Items.OfType<ResultsType>().FirstOrDefault();

            if (resultType == null) throw new Exception("Cannot get the ResultsType from the TRX file");

            var unitTestResultType = resultType.Items.OfType<UnitTestResultType>().FirstOrDefault();

            if (unitTestResultType == null) throw new Exception("Cannot get the UnitTestResultType from the TRX file");

            var testResult = unitTestResultType.outcome;

            if (!testResult.ToLower().Equals("failed")) return true;

            errorMsg = ((System.Xml.XmlNode[])(((OutputType)(unitTestResultType.Items[0])).ErrorInfo.Message))[0].Value;

            return false;
        }

        /// <summary>
        /// Cleans up all folder directories in the TestResults folder
        /// </summary>
        public void CleanUpDirectories(string resultFilePath)
        {
            try
            {
                var filePaths = Directory.GetDirectories(resultFilePath);

                foreach (var folder in filePaths)
                {
                    CleanDirectory(new DirectoryInfo(folder));
                    Directory.Delete(folder);
                }

            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch
            {

            }
        }

        /// <summary>
        /// Returns the name of the attribute that defines a description for a test method
        /// </summary>
        /// <returns>The name of the attribute that defines description for a test method</returns>
        public string GetTestMethodDescriptionAttribute()
        {
            return typeof(DescriptionAttribute).FullName;
        }

        /// <summary>
        /// Returns the name of the test runner
        /// </summary>
        /// <returns></returns>
        public string GetDisplayName()
        {
            return "MSTest";
        }

        /// <summary>
        /// Returns if the runner can export results to excel or not
        /// </summary>
        /// <returns></returns>
        public bool CanExportResultsToExcel()
        {
            return true;
        }

        /// <summary>
        /// Exports the results files of a folder to excel
        /// </summary>
        /// <param name="resultsPath"></param>
        /// <param name="excelFilepath"></param>
        public void ExportResultsToExcel(string resultsPath, string excelFilepath)
        {
            int aborted = 0, passed = 0, failed = 0, notexecuted = 0;

            // Get a refrence to Excel
            File.Delete(excelFilepath);
            var newFile = new FileInfo(excelFilepath);
            var oXl = new ExcelPackage(newFile);

            // Create a workbook and add sheet
            var oSheet = oXl.Workbook.Worksheets.Add("TRX");

            oSheet.Name = "trx";

            // Write the column names to the work sheet
            oSheet.Cells[1, 1].Value = "Processed File Name";
            oSheet.Cells[1, 2].Value = "Duration";
            oSheet.Cells[1, 3].Value = "Test ID";
            oSheet.Cells[1, 4].Value = "Test Name";
            oSheet.Cells[1, 5].Value = "Test Class";
            oSheet.Cells[1, 6].Value = "Test Outcome";
            oSheet.Cells[1, 7].Value = "Test Error";

            var row = 2;

            // For each .trx file in the given folder process it
            var filesList = Directory.GetFiles(resultsPath, "*.trx", SearchOption.AllDirectories);

            foreach (var file in filesList)
            {
                // Deserialize TestRunType object from the trx file
                var fileStreamReader = new StreamReader(file);

                var xmlSer = new XmlSerializer(typeof(TestRunType));

                var testRunType = (TestRunType)xmlSer.Deserialize(fileStreamReader);

                if (!testRunType.Items.OfType<ResultsType>().Any()) continue;

                var resultType = testRunType.Items.OfType<ResultsType>().FirstOrDefault();

                if (resultType == null || !resultType.Items.OfType<UnitTestResultType>().Any()) continue;

                if (!testRunType.Items.OfType<TestDefinitionType>().Any()) continue;

                var testDefinition = testRunType.Items.OfType<TestDefinitionType>().FirstOrDefault();

                var unitTestResultType = resultType.Items.OfType<UnitTestResultType>().FirstOrDefault();

                if (unitTestResultType == null) continue;

                var className = string.Empty;

                if (testDefinition != null)
                {
                    var testType = testDefinition.Items.OfType<UnitTestType>().FirstOrDefault();

                    if (testType != null)
                    {
                        className = testType.TestMethod.className;
                    }
                }

                oSheet.Cells[row, 1].Value = file;
                oSheet.Cells[row, 2].Value = unitTestResultType.duration;
                oSheet.Cells[row, 3].Value = unitTestResultType.testId;
                oSheet.Cells[row, 4].Value = unitTestResultType.testName;
                oSheet.Cells[row, 5].Value = className;
                oSheet.Cells[row, 6].Value = unitTestResultType.outcome;


                if (0 == String.Compare(unitTestResultType.outcome, "Aborted", StringComparison.Ordinal))
                {
                    oSheet.Cells[row, 1, row, 7].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    oSheet.Cells[row, 1, row, 7].Style.Fill.BackgroundColor.SetColor(Color.Yellow);
                    aborted++;
                }

                else if (0 == String.Compare(unitTestResultType.outcome, "Passed", StringComparison.Ordinal))
                {
                    oSheet.Cells[row, 1, row, 7].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    oSheet.Cells[row, 1, row, 7].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(198, 239, 206));
                    passed++;
                }

                else if (0 == String.Compare(unitTestResultType.outcome, "Failed", StringComparison.Ordinal))
                {
                    oSheet.Cells[row, 1, row, 7].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    oSheet.Cells[row, 7].Value = ((System.Xml.XmlNode[])(((OutputType)(unitTestResultType.Items[0])).ErrorInfo.Message))[0].Value;
                    oSheet.Cells[row, 1, row, 7].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 199, 206));
                    failed++;
                }

                else if (0 == String.Compare(unitTestResultType.outcome, "NotExecuted", StringComparison.Ordinal))
                {
                    oSheet.Cells[row, 1, row, 7].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    oSheet.Cells[row, 1, row, 7].Style.Fill.BackgroundColor.SetColor((Color.SlateGray));
                    notexecuted++;
                }

                row++;
            }

            row += 2;

            // Add summmary
            oSheet.Cells[row++, 1].Value = "Testcases Passed = " + passed;
            oSheet.Cells[row++, 1].Value = "Testcases Failed = " + failed;
            oSheet.Cells[row++, 1].Value = "Testcases Aborted = " + aborted;
            oSheet.Cells[row++, 1].Value = "Testcases NotExecuted = " + notexecuted;

            // Autoformat the sheet
            oSheet.Cells[1, 1, row, 7].AutoFitColumns();

            oXl.Save();
        }

        /// <summary>
        /// Cleans a single directory content
        /// </summary>
        /// <param name="directory"></param>
        private static void CleanDirectory(DirectoryInfo directory)
        {
            foreach (var file in directory.GetFiles())
            {
                file.Delete();
            }
            foreach (var subDirectory in directory.GetDirectories())
            {
                subDirectory.Delete(true);
            }
        }

    }
}
