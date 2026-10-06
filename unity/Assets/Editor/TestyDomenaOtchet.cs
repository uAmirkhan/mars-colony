using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace MarsColony.EditorTools
{
    /// <summary>
    /// Прогон EditMode-тестов домена из RunCommand с отчётом в файл. Слушатель регистрируется при каждой
    /// загрузке домена ([InitializeOnLoad]): запуск тестов сам вызывает перекомпиляцию и reload, и
    /// слушатель, зарегистрированный только в момент запуска, до конца прогона не доживает.
    /// Итог: <c>mars-colony/loop/ui/noch3/testy-domena.txt</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class TestyDomenaOtchet
    {
        public const string PUT = "C:/Ai/Jarvis/mars-colony/loop/ui/noch3/testy-domena.txt";

        static TestyDomenaOtchet()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Slushatel());
        }

        [MenuItem("Mars/Тесты домена (EditMode) → файл")]
        public static void Zapustit()
        {
            if (File.Exists(PUT)) File.Delete(PUT);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
        }

        private sealed class Slushatel : ICallbacks
        {
            private int _ok, _fail;
            private string _log = "";

            public void RunStarted(ITestAdaptor t) { _ok = 0; _fail = 0; _log = ""; }

            public void RunFinished(ITestResultAdaptor r)
            {
                _log += "ИТОГ: passed=" + _ok + " failed=" + _fail + " status=" + r.TestStatus + " duration=" + r.Duration.ToString("0.0") + "s" + System.Environment.NewLine;
                File.WriteAllText(PUT, _log);
                Debug.Log("[тесты] " + _log);
            }

            public void TestStarted(ITestAdaptor t) { }

            public void TestFinished(ITestResultAdaptor r)
            {
                if (r.HasChildren) return;
                if (r.TestStatus == TestStatus.Passed) _ok++;
                else { _fail++; _log += "FAIL " + r.FullName + ": " + r.Message + System.Environment.NewLine; }
            }
        }
    }
}
