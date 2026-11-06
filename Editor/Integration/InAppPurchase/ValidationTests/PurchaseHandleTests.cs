using System;
using NUnit.Framework;

namespace Com.Scheherazade.Common.Integration.InAppPurchase.Validation.Editor.Tests
{
    public sealed class PurchaseHandleTests
    {
        [Test]
        public void Complete_PublishesOneImmutableCorrelatedResult()
        {
            var source = new PurchaseHandleSource("coins");
            PurchaseHandle handle = source.Handle;
            int changed = 0;
            int completed = 0;
            handle.StatusChanged += _ => changed++;
            handle.Completed += _ => completed++;

            Assert.That(source.TryBindTransaction("GooglePlay:transaction"), Is.True);
            Assert.That(source.TryComplete(
                PurchaseStatus.Confirmed,
                transactionId: "GooglePlay:transaction"
            ), Is.True);
            Assert.That(source.TryComplete(PurchaseStatus.Failed, "late failure"), Is.False);

            PurchaseResult result = handle.Completion.Result;
            Assert.That(handle.IsCompleted, Is.True);
            Assert.That(result.Status, Is.EqualTo(PurchaseStatus.Confirmed));
            Assert.That(result.ProductId, Is.EqualTo("coins"));
            Assert.That(result.TransactionId, Is.EqualTo("GooglePlay:transaction"));
            Assert.That(changed, Is.EqualTo(2));
            Assert.That(completed, Is.EqualTo(1));
        }

        [Test]
        public void BindTransaction_RejectsIdentityChange()
        {
            var source = new PurchaseHandleSource("coins");
            PurchaseHandle handle = source.Handle;

            Assert.That(source.TryBindTransaction("first"), Is.True);
            Assert.That(source.TryBindTransaction("first"), Is.True);
            Assert.That(source.TryBindTransaction("second"), Is.False);
            Assert.That(handle.TransactionId, Is.EqualTo("first"));
        }

        [Test]
        public void ListenerException_DoesNotPreventCompletion()
        {
            var source = new PurchaseHandleSource("coins");
            PurchaseHandle handle = source.Handle;
            int completed = 0;
            handle.Completed += _ => throw new InvalidOperationException("Expected listener failure.");
            handle.Completed += _ => completed++;

            Assert.That(source.TryComplete(PurchaseStatus.Unavailable, "offline"), Is.True);
            Assert.That(handle.Completion.Result.Status, Is.EqualTo(PurchaseStatus.Unavailable));
            Assert.That(completed, Is.EqualTo(1));
        }
    }
}
