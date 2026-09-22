using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.Processing;
using NUnit.Framework;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.InAppPurchase.Validation.Editor.Tests
{
    public sealed class ReceiptValidationPipelineTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<ScriptableObject> _created = new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (ScriptableObject item in _created)
                if (item != null) UnityEngine.Object.DestroyImmediate(item);
            _created.Clear();
        }

        [Test]
        public void OrderedSteps_PassInDeclaredOrder()
        {
            var calls = new List<string>();
            FakeValidationStep first = Step("first", false, context =>
            {
                calls.Add("first");
                return SetReceipt(context, "effective-receipt");
            });
            FakeValidationStep second = Step("second", true, context =>
            {
                calls.Add("second");
                return SetId(context, "stable-order");
            });
            InAppPurchaseReceiptValidationPipeline pipeline = Pipeline(true, first, second);

            InAppPurchaseVerificationOutcome outcome = pipeline.Verify(
                Order(), false, out VerifiedInAppPurchaseTransaction transaction, out string reason
            );

            Assert.That(outcome, Is.EqualTo(InAppPurchaseVerificationOutcome.Verified), reason);
            Assert.That(calls, Is.EqualTo(new[] { "first", "second" }));
            Assert.That(transaction.TransactionId, Is.EqualTo("stable-order"));
            Assert.That(transaction.Receipt, Is.EqualTo("effective-receipt"));
        }

        [Test]
        public void RejectedStep_FailsFastWithoutRunningLaterSteps()
        {
            int laterCalls = 0;
            FakeValidationStep first = Step("first", false, _ => InAppPurchaseReceiptValidationResult.Passed());
            FakeValidationStep reject = Step("reject", false, _ =>
                InAppPurchaseReceiptValidationResult.Rejected("expected rejection"));
            FakeValidationStep later = Step("later", true, context =>
            {
                laterCalls++;
                return SetId(context, "must-not-run");
            });
            InAppPurchaseReceiptValidationPipeline pipeline = Pipeline(false, first, reject, later);

            InAppPurchaseVerificationOutcome outcome = pipeline.Verify(Order(), false, out _, out string reason);

            Assert.That(outcome, Is.EqualTo(InAppPurchaseVerificationOutcome.Rejected));
            Assert.That(reason, Is.EqualTo("expected rejection"));
            Assert.That(laterCalls, Is.Zero);
        }

        [Test]
        public void PlatformEntries_RunIndependentOrderedLists()
        {
            var calls = new List<string>();
            FakeValidationStep androidOne = Step("android-1", false, context =>
            {
                calls.Add("android-1");
                return InAppPurchaseReceiptValidationResult.Passed();
            }, RuntimePlatform.Android);
            FakeValidationStep androidTwo = Step("android-2", true, context =>
            {
                calls.Add("android-2");
                return SetId(context, "android-stable");
            }, RuntimePlatform.Android);
            FakeValidationStep ios = Step("ios", true, context =>
            {
                calls.Add("ios");
                return SetId(context, "ios-stable");
            }, RuntimePlatform.IPhonePlayer);
            FakeValidationStep editor = Step("editor", true, context =>
            {
                calls.Add("editor");
                return SetId(context, "editor-stable");
            }, RuntimePlatform.WindowsEditor);
            InAppPurchaseReceiptValidationPipeline pipeline = PipelineWithEntries(
                true,
                Entry(RuntimePlatform.Android, androidOne, androidTwo),
                Entry(RuntimePlatform.IPhonePlayer, ios),
                Entry(RuntimePlatform.WindowsEditor, editor)
            );

            Assert.That(pipeline.Verify(Order(RuntimePlatform.IPhonePlayer), false, out _, out string reason),
                Is.EqualTo(InAppPurchaseVerificationOutcome.Verified), reason);
            Assert.That(calls, Is.EqualTo(new[] { "ios" }));
            Assert.That(pipeline.StepsByPlatform.Keys,
                Is.EquivalentTo(new[] { RuntimePlatform.Android, RuntimePlatform.IPhonePlayer, RuntimePlatform.WindowsEditor }));
            Assert.That(pipeline.Steps.Count, Is.EqualTo(4));
        }

        [Test]
        public void MissingPlatformEntry_IsRejectedWithoutFallback()
        {
            FakeValidationStep android = Step("android", true, context => SetId(context, "android"),
                RuntimePlatform.Android);
            InAppPurchaseReceiptValidationPipeline pipeline = PipelineWithEntries(
                true, Entry(RuntimePlatform.Android, android)
            );

            InAppPurchaseVerificationOutcome outcome = pipeline.Verify(
                Order(RuntimePlatform.IPhonePlayer), false, out _, out string reason
            );

            Assert.That(outcome, Is.EqualTo(InAppPurchaseVerificationOutcome.Rejected));
            StringAssert.Contains("no receipt validation pipeline entry", reason.ToLowerInvariant());
        }

        [Test]
        public void DuplicatePlatformEntry_IsRejected()
        {
            FakeValidationStep first = Step("first", true, context => SetId(context, "first"));
            FakeValidationStep second = Step("second", true, context => SetId(context, "second"));
            InAppPurchaseReceiptValidationPipeline pipeline = PipelineWithEntries(
                true,
                Entry(RuntimePlatform.WindowsEditor, first),
                Entry(RuntimePlatform.WindowsEditor, second)
            );
            Assert.That(pipeline.ValidateConfiguration(RuntimePlatform.WindowsEditor, out string reason), Is.False);
            StringAssert.Contains("duplicate", reason.ToLowerInvariant());
        }

        [Test]
        public void StepAcceptedPlatformsMustIncludeEntryPlatform()
        {
            FakeValidationStep iosOnly = Step("ios", true, context => SetId(context, "ios"),
                RuntimePlatform.IPhonePlayer);
            InAppPurchaseReceiptValidationPipeline pipeline = PipelineWithEntries(
                true, Entry(RuntimePlatform.Android, iosOnly)
            );
            Assert.That(pipeline.ValidateConfiguration(RuntimePlatform.Android, out string reason), Is.False);
            StringAssert.Contains("does not accept", reason.ToLowerInvariant());
        }


        [Test]
        public void NullStepList_IsRejected()
        {
            InAppPurchaseReceiptValidationPlatformSteps entry = Entry(RuntimePlatform.WindowsEditor);
            Set(entry, "steps", null);
            InAppPurchaseReceiptValidationPipeline pipeline = PipelineWithEntries(true, entry);
            Assert.That(pipeline.ValidateConfiguration(RuntimePlatform.WindowsEditor, out string reason), Is.False);
            StringAssert.Contains("steps are missing", reason.ToLowerInvariant());
        }


        [Test]
        public void NullStepElement_IsRejected()
        {
            InAppPurchaseReceiptValidationPipeline pipeline = PipelineWithEntries(
                true, Entry(RuntimePlatform.WindowsEditor, (InAppPurchaseReceiptValidationStep)null)
            );
            Assert.That(pipeline.ValidateConfiguration(RuntimePlatform.WindowsEditor, out string reason), Is.False);
            StringAssert.Contains("step 0 is missing", reason.ToLowerInvariant());
        }

        [Test]
        public void ConflictingStableIdentities_AreRejected()
        {
            FakeValidationStep first = Step("first", true, context => SetId(context, "stable-a"));
            FakeValidationStep second = Step("second", true, context => SetId(context, "stable-b"));
            InAppPurchaseReceiptValidationPipeline pipeline = Pipeline(true, first, second);

            InAppPurchaseVerificationOutcome outcome = pipeline.Verify(Order(), false, out _, out string reason);

            Assert.That(outcome, Is.EqualTo(InAppPurchaseVerificationOutcome.Rejected));
            StringAssert.Contains("conflicting", reason.ToLowerInvariant());
        }

        [Test]
        public void VerifiedTransaction_PreservesExactCartOrderAndValues()
        {
            InAppPurchaseLineItem[] items =
            {
                new InAppPurchaseLineItem("logical-a", "store-a", 2, false),
                new InAppPurchaseLineItem("logical-b", "store-b", 1, true)
            };
            FakeValidationStep identity = Step("identity", true, context => SetId(context, "stable-cart"));
            InAppPurchaseReceiptValidationPipeline pipeline = Pipeline(true, identity);

            InAppPurchaseVerificationOutcome outcome = pipeline.Verify(
                Order(RuntimePlatform.WindowsEditor, InAppPurchaseOrderSource.Simulated, items),
                false, out VerifiedInAppPurchaseTransaction transaction, out string reason
            );

            Assert.That(outcome, Is.EqualTo(InAppPurchaseVerificationOutcome.Verified), reason);
            Assert.That(transaction.Items.Count, Is.EqualTo(items.Length));
            for (int i = 0; i < items.Length; i++)
            {
                Assert.That(transaction.Items[i].ProductId, Is.EqualTo(items[i].ProductId));
                Assert.That(transaction.Items[i].StoreProductId, Is.EqualTo(items[i].StoreProductId));
                Assert.That(transaction.Items[i].Quantity, Is.EqualTo(items[i].Quantity));
                Assert.That(transaction.Items[i].AllowRecover, Is.EqualTo(items[i].AllowRecover));
            }
        }

        [Test]
        public void StoreKit2_DirectWaitsAndFetchedJwsPasses()
        {
            StoreKit2ReceiptValidationStep storeKit = Create<StoreKit2ReceiptValidationStep>();
            SetPlatforms(storeKit, RuntimePlatform.IPhonePlayer);
            InAppPurchaseReceiptValidationPipeline pipeline = PipelineWithEntries(
                true, Entry(RuntimePlatform.IPhonePlayer, storeKit)
            );
            InAppPurchaseOrderData direct = Order(
                RuntimePlatform.IPhonePlayer, InAppPurchaseOrderSource.Direct,
                jws: "direct-jws", transactionId: "apple-transaction"
            );
            Assert.That(pipeline.Verify(direct, false, out _, out _),
                Is.EqualTo(InAppPurchaseVerificationOutcome.WaitForStore));

            InAppPurchaseOrderData fetched = Order(
                RuntimePlatform.IPhonePlayer, InAppPurchaseOrderSource.Fetched,
                jws: "fetched-jws", transactionId: "apple-transaction"
            );
            InAppPurchaseVerificationOutcome outcome = pipeline.Verify(
                fetched, false, out VerifiedInAppPurchaseTransaction transaction, out string reason
            );
            Assert.That(outcome, Is.EqualTo(InAppPurchaseVerificationOutcome.Verified), reason);
            Assert.That(transaction.TransactionId, Is.EqualTo("AppleAppStore:apple-transaction"));
            Assert.That(transaction.Receipt, Is.EqualTo("fetched-jws"));
            Assert.That(transaction.Jws, Is.EqualTo("fetched-jws"));
        }

        [Test]
        public void EditorSimulated_AcceptsOnlyEditorPseudoPurchaseAndNeverRestoration()
        {
            EditorSimulatedReceiptValidationStep editor = Create<EditorSimulatedReceiptValidationStep>();
            SetPlatforms(editor, RuntimePlatform.WindowsEditor);
            InAppPurchaseReceiptValidationPipeline pipeline = Pipeline(true, editor);
            InAppPurchaseOrderData valid = Order(
                RuntimePlatform.WindowsEditor, InAppPurchaseOrderSource.Simulated,
                transactionId: "EditorPseudo:one"
            );
            Assert.That(pipeline.Verify(valid, false, out VerifiedInAppPurchaseTransaction transaction, out _),
                Is.EqualTo(InAppPurchaseVerificationOutcome.Verified));
            Assert.That(transaction.TransactionId, Is.EqualTo("EditorPseudo:one"));

            Assert.That(pipeline.Verify(Order(RuntimePlatform.WindowsEditor, InAppPurchaseOrderSource.Direct,
                    transactionId: "EditorPseudo:direct"), false, out _, out _),
                Is.EqualTo(InAppPurchaseVerificationOutcome.Rejected));
            Assert.That(pipeline.Verify(valid, true, out _, out _),
                Is.EqualTo(InAppPurchaseVerificationOutcome.Rejected));
            Assert.That(pipeline.Verify(Order(RuntimePlatform.WindowsEditor, InAppPurchaseOrderSource.Simulated,
                    transactionId: "wrong-prefix"), false, out _, out _),
                Is.EqualTo(InAppPurchaseVerificationOutcome.Rejected));
        }

        [Test]
        public void GoogleStep_MissingTangleFailsConfigurationClosed()
        {
            MissingGoogleTangleStep google = Create<MissingGoogleTangleStep>();
            SetPlatforms(google, RuntimePlatform.Android);
            InAppPurchaseReceiptValidationPipeline pipeline = PipelineWithEntries(
                true, Entry(RuntimePlatform.Android, google)
            );

            InAppPurchaseVerificationOutcome outcome = pipeline.Verify(
                Order(RuntimePlatform.Android, InAppPurchaseOrderSource.Direct, receipt: "unsigned-fixture"),
                false, out _, out string reason
            );

            Assert.That(outcome, Is.EqualTo(InAppPurchaseVerificationOutcome.Rejected));
            StringAssert.Contains("tangle", reason.ToLowerInvariant());
        }

        [TestCase(InAppPurchaseReceiptValidationOutcome.Retry, InAppPurchaseVerificationOutcome.Retry)]
        [TestCase(InAppPurchaseReceiptValidationOutcome.WaitForStore, InAppPurchaseVerificationOutcome.WaitForStore)]
        public void StepOutcome_IsMappedWithoutReceiptFabrication(
            InAppPurchaseReceiptValidationOutcome stepOutcome,
            InAppPurchaseVerificationOutcome expected
        )
        {
            FakeValidationStep step = Step("mapping", true, _ => stepOutcome ==
                InAppPurchaseReceiptValidationOutcome.Retry
                    ? InAppPurchaseReceiptValidationResult.Retry("retry")
                    : InAppPurchaseReceiptValidationResult.WaitForStore("wait"));
            InAppPurchaseReceiptValidationPipeline pipeline = Pipeline(true, step);
            Assert.That(pipeline.Verify(Order(), false, out _, out _), Is.EqualTo(expected));
        }

        [Test]
        public void VerifiedReceiptPipeline_FlowsThroughFulfillAcknowledgePublish()
        {
            var sequence = new List<string>();
            FakeValidationStep identity = Step("identity", true, context =>
            {
                sequence.Add("validate");
                return SetId(context, "stable-processing");
            });
            InAppPurchaseReceiptValidationPipeline verifier = Pipeline(true, identity);
            var fulfillment = new RecordingFulfillment(sequence);
            var options = new InAppPurchaseProcessingOptions(verifier, fulfillment);
            InAppPurchaseTransactionPipeline processing = null;
            processing = new InAppPurchaseTransactionPipeline(
                options,
                (raw, transaction) =>
                {
                    sequence.Add("acknowledge");
                    processing.ReportAcknowledgement(transaction.TransactionId, transaction.IsRestoration, true);
                },
                _ => sequence.Add("publish")
            );

            InAppPurchasePipelineResult result = processing.Process(Order());

            Assert.That(result.Outcome, Is.EqualTo(InAppPurchasePipelineOutcome.Completed));
            Assert.That(result.IsPublished, Is.True);
            Assert.That(processing.IsAcknowledged("stable-processing", false), Is.True);
            Assert.That(sequence, Is.EqualTo(new[] { "validate", "fulfill", "acknowledge", "publish" }));
            processing.Dispose();
        }

        private InAppPurchaseReceiptValidationPipeline Pipeline(
            bool requireValidation,
            params InAppPurchaseReceiptValidationStep[] steps
        ) => PipelineWithEntries(requireValidation, Entry(RuntimePlatform.WindowsEditor, steps));

        private InAppPurchaseReceiptValidationPipeline PipelineWithEntries(
            bool requireValidation,
            params InAppPurchaseReceiptValidationPlatformSteps[] entries
        )
        {
            InAppPurchaseReceiptValidationPipeline pipeline = Create<InAppPurchaseReceiptValidationPipeline>();
            Set(pipeline, "platformSteps", entries.ToList());
            Set(pipeline, "requireValidation", requireValidation);
            return pipeline;
        }

        private static InAppPurchaseReceiptValidationPlatformSteps Entry(
            RuntimePlatform platform,
            params InAppPurchaseReceiptValidationStep[] steps
        )
        {
            var entry = new InAppPurchaseReceiptValidationPlatformSteps();
            Set(entry, "platform", platform);
            Set(entry, "steps", steps?.ToList());
            return entry;
        }

        private FakeValidationStep Step(
            string name,
            bool authenticity,
            Func<InAppPurchaseReceiptValidationContext, InAppPurchaseReceiptValidationResult> validate,
            params RuntimePlatform[] platforms
        )
        {
            FakeValidationStep step = Create<FakeValidationStep>();
            step.StepName = name;
            step.Authenticity = authenticity;
            step.Validation = validate;
            SetPlatforms(step, platforms == null || platforms.Length == 0
                ? new[] { RuntimePlatform.WindowsEditor }
                : platforms);
            return step;
        }

        private T Create<T>() where T : ScriptableObject
        {
            T instance = ScriptableObject.CreateInstance<T>();
            _created.Add(instance);
            return instance;
        }

        private static void SetPlatforms(
            InAppPurchaseReceiptValidationStep step,
            params RuntimePlatform[] platforms
        ) => Set(step, "acceptedPlatforms", platforms ?? Array.Empty<RuntimePlatform>(),
            typeof(InAppPurchaseReceiptValidationStep));

        private static void Set(object target, string field, object value, Type declaringType = null)
        {
            FieldInfo info = (declaringType ?? target.GetType()).GetField(field, PrivateInstance);
            if (info == null) throw new MissingFieldException((declaringType ?? target.GetType()).Name, field);
            info.SetValue(target, value);
        }

        private static InAppPurchaseOrderData Order(
            RuntimePlatform platform = RuntimePlatform.WindowsEditor,
            InAppPurchaseOrderSource source = InAppPurchaseOrderSource.Simulated,
            IEnumerable<InAppPurchaseLineItem> items = null,
            string receipt = "receipt",
            string jws = "",
            string transactionId = "EditorPseudo:transaction"
        ) => new InAppPurchaseOrderData(
            transactionId,
            receipt,
            jws,
            platform,
            source,
            InAppPurchaseOrderKind.Pending,
            items ?? new[] { new InAppPurchaseLineItem("logical", "store", 1, false) }
        );

        private static InAppPurchaseReceiptValidationResult SetId(
            InAppPurchaseReceiptValidationContext context,
            string id
        ) => context.TrySetStableTransactionId(id, out string reason)
            ? InAppPurchaseReceiptValidationResult.Passed()
            : InAppPurchaseReceiptValidationResult.Rejected(reason);

        private static InAppPurchaseReceiptValidationResult SetReceipt(
            InAppPurchaseReceiptValidationContext context,
            string receipt
        ) => context.TrySetEffectiveReceipt(receipt, out string reason)
            ? InAppPurchaseReceiptValidationResult.Passed()
            : InAppPurchaseReceiptValidationResult.Rejected(reason);
    }

    internal sealed class FakeValidationStep : InAppPurchaseReceiptValidationStep
    {
        internal string StepName;
        internal bool Authenticity;
        internal Func<InAppPurchaseReceiptValidationContext, InAppPurchaseReceiptValidationResult> Validation;
        public override bool ProvidesAuthenticity => Authenticity;
        public override InAppPurchaseReceiptValidationResult Validate(
            InAppPurchaseReceiptValidationContext context
        ) => Validation?.Invoke(context) ?? InAppPurchaseReceiptValidationResult.Rejected(
            "Fake step " + StepName + " has no validation delegate."
        );
    }

    internal sealed class MissingGoogleTangleStep : GooglePlayTangleReceiptValidationStepBase
    {
        protected override byte[] TangleData => Array.Empty<byte>();
    }

    internal sealed class RecordingFulfillment : IInAppPurchaseTransactionFulfillment
    {
        private readonly IList<string> _sequence;
        internal RecordingFulfillment(IList<string> sequence) => _sequence = sequence;
        public InAppPurchaseProcessingOutcome Fulfill(
            VerifiedInAppPurchaseTransaction transaction,
            out string reason
        )
        {
            _sequence.Add("fulfill");
            reason = string.Empty;
            return InAppPurchaseProcessingOutcome.Completed;
        }
    }
}
