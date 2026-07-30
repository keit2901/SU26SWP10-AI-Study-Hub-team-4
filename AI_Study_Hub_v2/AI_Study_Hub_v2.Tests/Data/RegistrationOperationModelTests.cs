using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AI_Study_Hub_v2.Tests.Schema;

[TestFixture]
public sealed class RegistrationOperationModelTests
{
    [Test]
    public void Model_MapsDurableRegistrationOperationTableAndRequiredColumns()
    {
        using var db = TestDb.CreateInMemory();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(RegistrationOperation))
            ?? throw new InvalidOperationException("Registration operation metadata is missing.");

        entity.GetTableName().Should().Be("registration_operations");
        entity.FindPrimaryKey()!.Properties.Select(property => property.Name).Should().Equal(nameof(RegistrationOperation.Id));
        entity.FindProperty(nameof(RegistrationOperation.NormalizedEmail))!.GetColumnName().Should().Be("normalized_email");
        entity.FindProperty(nameof(RegistrationOperation.ProfileUserId))!.GetColumnName().Should().Be("profile_user_id");
        entity.FindProperty(nameof(RegistrationOperation.IdentityId))!.IsNullable.Should().BeTrue();
        entity.FindProperty(nameof(RegistrationOperation.Status))!.GetMaxLength().Should().Be(32);
        entity.FindProperty(nameof(RegistrationOperation.Id))!.ValueGenerated.Should().Be(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never);
        entity.GetForeignKeys().Should().BeEmpty();
    }

    [Test]
    public void Model_UsesRequiredDurableConstraintsAndFilteredIndexes()
    {
        using var db = TestDb.CreateInMemory();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(RegistrationOperation))!;
        var constraints = entity.GetCheckConstraints().ToDictionary(constraint => constraint.Name!, constraint => constraint.Sql);

        constraints.Should().ContainKey("ck_registration_operations_status");
        constraints["ck_registration_operations_status"].Should().Be("status IN ('Prepared', 'CreatingIdentity', 'IdentityConfirmed', 'FinalizingProfile', 'ProfileCommitted', 'Completed', 'CompensationRequired', 'Compensating', 'Compensated', 'Conflict', 'Expired')");
        constraints.Should().ContainKey("ck_registration_operations_attempt_count_non_negative");
        constraints.Should().ContainKey("ck_registration_operations_lease_pair");
        constraints.Should().ContainKey("ck_registration_operations_identity_required");
        constraints["ck_registration_operations_identity_required"].Should().ContainAll(
            RegistrationOperation.IdentityConfirmed, RegistrationOperation.FinalizingProfile, RegistrationOperation.ProfileCommitted,
            RegistrationOperation.Completed, RegistrationOperation.CompensationRequired, RegistrationOperation.Compensating);
        constraints.Should().ContainKey("ck_registration_operations_id_non_empty");
        constraints.Should().ContainKey("ck_registration_operations_profile_user_id_non_empty");
        constraints.Should().ContainKey("ck_registration_operations_identity_id_non_empty");
        constraints.Should().ContainKey("ck_registration_operations_lease_token_non_empty");
        const string emptyUuid = "'00000000-0000-0000-0000-000000000000'::uuid";
        constraints["ck_registration_operations_id_non_empty"].Should().Be($"id <> {emptyUuid}");
        constraints["ck_registration_operations_profile_user_id_non_empty"].Should().Be($"profile_user_id <> {emptyUuid}");
        constraints["ck_registration_operations_identity_id_non_empty"].Should().Be($"identity_id IS NULL OR identity_id <> {emptyUuid}");
        constraints["ck_registration_operations_lease_token_non_empty"].Should().Be($"lease_token IS NULL OR lease_token <> {emptyUuid}");

        entity.GetIndexes().Single(index => index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(RegistrationOperation.ProfileUserId) }))
            .IsUnique.Should().BeTrue();
        var identityIndex = entity.GetIndexes().Single(index => index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(RegistrationOperation.IdentityId) }));
        identityIndex.IsUnique.Should().BeTrue();
        identityIndex.GetFilter().Should().Contain("identity_id IS NOT NULL");
        const string terminalExclusion = "status NOT IN ('Compensated', 'Conflict', 'Expired')";
        entity.GetIndexes().Single(index => index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(RegistrationOperation.NormalizedEmail) }))
            .GetFilter().Should().Be(terminalExclusion);
        entity.GetIndexes().Single(index => index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(RegistrationOperation.Username) }))
            .GetFilter().Should().Be(terminalExclusion);
        entity.GetIndexes().Should().Contain(index => index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(RegistrationOperation.Status), nameof(RegistrationOperation.NextAttemptAt), nameof(RegistrationOperation.UpdatedAt) }));
        entity.GetIndexes().Should().Contain(index => index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(RegistrationOperation.LeaseExpiresAt) }));
    }
}
