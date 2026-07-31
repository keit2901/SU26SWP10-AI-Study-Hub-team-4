using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AI_Study_Hub_v2.Tests.Schema;

[TestFixture]
public sealed class UserNotificationModelTests
{
    [Test]
    public void Model_maps_notification_constraints_relationships_and_indexes()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(UserNotification))!;

        entity.GetTableName().Should().Be("user_notifications");
        entity.FindProperty(nameof(UserNotification.RecipientUserId))!.GetColumnName().Should().Be("recipient_user_id");
        entity.FindProperty(nameof(UserNotification.EventKey))!.IsNullable.Should().BeFalse();
        entity.FindProperty(nameof(UserNotification.DocumentId))!.IsNullable.Should().BeTrue();
        entity.GetForeignKeys().Should().Contain(foreignKey => foreignKey.Properties.Single().Name == nameof(UserNotification.RecipientUserId) && foreignKey.DeleteBehavior == DeleteBehavior.Cascade);
        entity.GetForeignKeys().Should().Contain(foreignKey => foreignKey.Properties.Single().Name == nameof(UserNotification.FolderId) && foreignKey.DeleteBehavior == DeleteBehavior.Cascade);
        entity.GetForeignKeys().Should().Contain(foreignKey => foreignKey.Properties.Single().Name == nameof(UserNotification.DocumentId) && foreignKey.DeleteBehavior == DeleteBehavior.SetNull);
        entity.GetCheckConstraints().Select(constraint => constraint.Name).Should().Contain(new[]
        {
            "ck_user_notifications_kind",
            "ck_user_notifications_outcome",
            "ck_user_notifications_submission_number"
        });
        entity.GetIndexes().Should().Contain(index => index.Properties.Select(property => property.Name).SequenceEqual(new[]
        {
            nameof(UserNotification.EventKey)
        }) && index.IsUnique);
        entity.GetIndexes().Should().Contain(index => index.Properties.Select(property => property.Name).SequenceEqual(new[]
        {
            nameof(UserNotification.RecipientUserId), nameof(UserNotification.CreatedAt)
        }) && index.GetDatabaseName() == "ix_user_notifications_recipient_created_at");
        entity.GetIndexes().Should().Contain(index => index.Properties.Select(property => property.Name).SequenceEqual(new[]
        {
            nameof(UserNotification.RecipientUserId)
        }) && index.GetDatabaseName() == "ix_user_notifications_recipient_unread" && index.GetFilter() == "read_at IS NULL");
    }
}
