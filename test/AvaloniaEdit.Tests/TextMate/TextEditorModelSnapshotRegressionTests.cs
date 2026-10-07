using System;
using System.Threading.Tasks;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace AvaloniaEdit.Tests.TextMate
{
    [TestFixture]
    public class TextEditorModelSnapshotRegressionTests
    {
        [TestCase("hello", 0, 5, "")]
        [TestCase("hello", 1, 1, "X")]
        [TestCase("one\ntwo\nthree", 0, 13, "replacement")]
        [TestCase("one\ntwo\nthree", 4, 4, "")]
        [TestCase("one\r\ntwo\r\nthree", 3, 7, "\r\n")]
        [TestCase("one\r\ntwo", 4, 1, "")]
        [TestCase("one\r\ntwo", 3, 1, "")]
        [TestCase("\r\n", 1, 0, "split")]
        public void RemovingText_KeepsTheStartLineInBothModels(string text, int offset, int length, string replacement)
        {
            var document = new TextDocument(text);
            using var model = new TextEditorModel(new TextView(), document, ex => throw ex);
            var observed = false;
            document.Changing += (_, e) =>
            {
                var count = 0;
                model.ForEach(_ => count++);
                Assert.AreEqual(count, model.DocumentSnapshot.LineCount,
                    "The snapshot and token model must remove the same lines.");
                Assert.GreaterOrEqual(count, 1, "Every document retains a start line.");
                // A tokenization worker may read while the UI is between Changing and Changed.
                Assert.DoesNotThrow(() => Task.Run(() => model.GetLineLength(0)).GetAwaiter().GetResult());
                observed = true;
            };

            document.Replace(offset, length, replacement);

            Assert.IsTrue(observed);
            AssertSnapshotMatchesDocument(document, model.DocumentSnapshot);
        }

        [Test]
        public void RepeatedEdits_KeepAllSnapshotOffsetsAndTerminatorsCorrect()
        {
            var document = new TextDocument("first\r\nsecond\nthird");
            using var model = new TextEditorModel(new TextView(), document, ex => throw ex);
            var random = new Random(1923);
            var insertions = new[] { "", "x", "\n", "\r\n", "longer text", "a\nb\r\nc" };
            for (var i = 0; i < 500; i++)
            {
                var offset = random.Next(document.TextLength + 1);
                var length = random.Next(document.TextLength - offset + 1);
                var before = document.Text;
                document.Replace(offset, length, insertions[i % insertions.Length]);
                try
                {
                    AssertSnapshotMatchesDocument(document, model.DocumentSnapshot);
                }
                catch
                {
                    TestContext.WriteLine($"Edit {i}: [{before.Replace("\r", "\\r").Replace("\n", "\\n")}] offset={offset} length={length} insertion=[{insertions[i % insertions.Length].Replace("\r", "\\r").Replace("\n", "\\n")}]");
                    throw;
                }
                var count = 0;
                model.ForEach(_ => count++);
                Assert.AreEqual(document.LineCount, count);
            }
        }

        private static void AssertSnapshotMatchesDocument(TextDocument document, DocumentSnapshot snapshot)
        {
            Assert.AreEqual(document.LineCount, snapshot.LineCount);
            Assert.AreEqual(document.Text, snapshot.GetText());
            foreach (var line in document.Lines)
            {
                var index = line.LineNumber - 1;
                Assert.AreEqual(document.GetText(line), snapshot.GetLineText(index));
                Assert.AreEqual(document.GetText(line.Offset, line.TotalLength),
                    snapshot.GetLineTextIncludingTerminatorAsMemory(index).ToString());
                Assert.AreEqual(line.Length, snapshot.GetLineLength(index));
                Assert.AreEqual(line.TotalLength, snapshot.GetTotalLineLength(index));
            }
        }
    }
}
