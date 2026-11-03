using FluentAssertions;
using NSubstitute;
using Turning.Application.Exceptions;
using Turning.Application.Features.ExperimentSessions;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;
using Xunit;

namespace Turning.Application.Tests;

/// <summary>
/// Pruebas del servicio de sesiones experimentales.
/// </summary>
public class ExperimentSessionServiceTests
{
    private readonly IExperimentSessionRepository _experimentSessionRepository = Substitute.For<IExperimentSessionRepository>();

    [Fact]
    public async Task CreateBootstrapSessionAsync_ShouldPersistSessionWithInitialState()
    {
        var service = new ExperimentSessionService(_experimentSessionRepository, Microsoft.Extensions.Options.Options.Create(new SessionOptions()));
        var ownerUserId = Guid.NewGuid();

        // Act
        var result = await service.CreateBootstrapSessionAsync(ownerUserId, new CreateExperimentSessionRequest
        {
            PreferredCondition = "AI"
        });

        // Assert
        result.Condition.Should().Be("Hidden");
        result.Status.Should().Be("Created");
        result.AvatarState.Should().Be("Neutral");
        result.ConversationTurnCount.Should().Be(0);
        result.EmotionSampleCount.Should().Be(0);
        await _experimentSessionRepository.Received(1).AddAsync(Arg.Is<ExperimentSession>(session =>
            session.OwnerUserId == ownerUserId &&
            session.Condition == ExperimentalCondition.AI &&
            session.Status == ExperimentSessionStatus.Created), Arg.Any<CancellationToken>());
        await _experimentSessionRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListByParticipantAsync_ShouldThrowForbidden_WhenRequesterIsNotOwnerAndNotPrivileged()
    {
        var service = new ExperimentSessionService(_experimentSessionRepository, Microsoft.Extensions.Options.Options.Create(new SessionOptions()));
        var participantId = Guid.NewGuid();
        var requestingUserId = Guid.NewGuid();

        var act = () => service.ListByParticipantAsync(participantId, requestingUserId, isPrivilegedRequester: false, page: 1, pageSize: 50);

        var ex = await act.Should().ThrowAsync<Turning.Application.Exceptions.ApplicationException>();
        ex.Which.Code.Should().Be("SESSION_FORBIDDEN");
        await _experimentSessionRepository.DidNotReceive().ListByOwnerAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListByParticipantAsync_ShouldReturnSessions_WhenRequesterIsOwner()
    {
        var service = new ExperimentSessionService(_experimentSessionRepository, Microsoft.Extensions.Options.Options.Create(new SessionOptions()));
        var participantId = Guid.NewGuid();
        _experimentSessionRepository.ListByOwnerAsync(participantId, 1, 50, Arg.Any<CancellationToken>()).Returns(new List<ExperimentSession>());
        _experimentSessionRepository.CountByOwnerAsync(participantId, Arg.Any<CancellationToken>()).Returns(0);

        var result = await service.ListByParticipantAsync(participantId, requestingUserId: participantId, isPrivilegedRequester: false, page: 1, pageSize: 50);

        result.Total.Should().Be(0);
    }

    [Fact]
    public async Task ListByParticipantAsync_ShouldReturnSessions_WhenRequesterIsPrivileged()
    {
        var service = new ExperimentSessionService(_experimentSessionRepository, Microsoft.Extensions.Options.Options.Create(new SessionOptions()));
        var participantId = Guid.NewGuid();
        var researcherId = Guid.NewGuid();
        _experimentSessionRepository.ListByOwnerAsync(participantId, 1, 50, Arg.Any<CancellationToken>()).Returns(new List<ExperimentSession>());
        _experimentSessionRepository.CountByOwnerAsync(participantId, Arg.Any<CancellationToken>()).Returns(0);

        var result = await service.ListByParticipantAsync(participantId, requestingUserId: researcherId, isPrivilegedRequester: true, page: 1, pageSize: 50);

        result.Total.Should().Be(0);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldThrowNotFound_WhenRequesterIsNotOwner()
    {
        var service = CreateService();
        var session = ExperimentSession.Create(Guid.NewGuid(), ExperimentalCondition.AI);
        _experimentSessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var act = () => service.GetByIdAsync(session.Id, requestingUserId: Guid.NewGuid(), isPrivilegedRequester: false);

        var ex = await act.Should().ThrowAsync<Turning.Application.Exceptions.ApplicationException>();
        ex.Which.Code.Should().Be("SESSION_NOT_FOUND");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnSnapshot_WhenRequesterIsOwner()
    {
        var service = CreateService();
        var ownerId = Guid.NewGuid();
        var session = ExperimentSession.Create(ownerId, ExperimentalCondition.Human);
        _experimentSessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await service.GetByIdAsync(session.Id, requestingUserId: ownerId, isPrivilegedRequester: false);

        result.Id.Should().Be(session.Id);
        result.Condition.Should().Be("Hidden");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnSnapshot_WhenRequesterIsPrivileged()
    {
        var service = CreateService();
        var session = ExperimentSession.Create(Guid.NewGuid(), ExperimentalCondition.AI);
        _experimentSessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await service.GetByIdAsync(session.Id, requestingUserId: Guid.NewGuid(), isPrivilegedRequester: true);

        result.Id.Should().Be(session.Id);
    }

    [Fact]
    public async Task ActivateAsync_ShouldThrowNotFoundAndNotMutate_WhenRequesterIsNotOwner()
    {
        var service = CreateService();
        var session = ExperimentSession.Create(Guid.NewGuid(), ExperimentalCondition.AI);
        _experimentSessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var act = () => service.ActivateAsync(session.Id, requestingUserId: Guid.NewGuid(), isPrivilegedRequester: false);

        var ex = await act.Should().ThrowAsync<Turning.Application.Exceptions.ApplicationException>();
        ex.Which.Code.Should().Be("SESSION_NOT_FOUND");
        session.Status.Should().Be(ExperimentSessionStatus.Created);
        await _experimentSessionRepository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ActivateAsync_ShouldActivate_WhenRequesterIsOwner()
    {
        var service = CreateService();
        var ownerId = Guid.NewGuid();
        var session = ExperimentSession.Create(ownerId, ExperimentalCondition.AI);
        _experimentSessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await service.ActivateAsync(session.Id, requestingUserId: ownerId, isPrivilegedRequester: false);

        result.Status.Should().Be("Active");
        await _experimentSessionRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_ShouldThrowNotFoundAndNotMutate_WhenRequesterIsNotOwner()
    {
        var service = CreateService();
        var session = ExperimentSession.Create(Guid.NewGuid(), ExperimentalCondition.AI);
        session.Activate(TimeSpan.FromSeconds(300));
        _experimentSessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var act = () => service.CompleteAsync(session.Id, requestingUserId: Guid.NewGuid(), isPrivilegedRequester: false);

        var ex = await act.Should().ThrowAsync<Turning.Application.Exceptions.ApplicationException>();
        ex.Which.Code.Should().Be("SESSION_NOT_FOUND");
        session.Status.Should().Be(ExperimentSessionStatus.Active);
        await _experimentSessionRepository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_ShouldComplete_WhenRequesterIsOwner()
    {
        var service = CreateService();
        var ownerId = Guid.NewGuid();
        var session = ExperimentSession.Create(ownerId, ExperimentalCondition.AI);
        session.Activate(TimeSpan.FromSeconds(300));
        _experimentSessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await service.CompleteAsync(session.Id, requestingUserId: ownerId, isPrivilegedRequester: false);

        result.Status.Should().Be("Completed");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldThrowNotFound_WhenSessionDoesNotExist()
    {
        var service = CreateService();
        _experimentSessionRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ExperimentSession?)null);

        var act = () => service.GetByIdAsync(Guid.NewGuid(), requestingUserId: Guid.NewGuid(), isPrivilegedRequester: true);

        var ex = await act.Should().ThrowAsync<Turning.Application.Exceptions.ApplicationException>();
        ex.Which.Code.Should().Be("SESSION_NOT_FOUND");
    }

    // --- IsSessionAccessibleAsync: la regla que consumen las rutas anidadas ---

    [Fact]
    public async Task IsSessionAccessibleAsync_ShouldBeTrue_ForOwner()
    {
        var service = CreateService();
        var ownerUserId = Guid.NewGuid();
        var session = ExperimentSession.Create(ownerUserId, ExperimentalCondition.AI);
        _experimentSessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var accesible = await service.IsSessionAccessibleAsync(session.Id, ownerUserId, isPrivilegedRequester: false);

        accesible.Should().BeTrue();
    }

    [Fact]
    public async Task IsSessionAccessibleAsync_ShouldBeFalse_ForStranger()
    {
        var service = CreateService();
        var session = ExperimentSession.Create(Guid.NewGuid(), ExperimentalCondition.AI);
        _experimentSessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var accesible = await service.IsSessionAccessibleAsync(session.Id, Guid.NewGuid(), isPrivilegedRequester: false);

        accesible.Should().BeFalse();
    }

    [Fact]
    public async Task IsSessionAccessibleAsync_ShouldBeTrue_ForPrivilegedStranger()
    {
        var service = CreateService();
        var session = ExperimentSession.Create(Guid.NewGuid(), ExperimentalCondition.AI);
        _experimentSessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var accesible = await service.IsSessionAccessibleAsync(session.Id, Guid.NewGuid(), isPrivilegedRequester: true);

        accesible.Should().BeTrue();
    }

    /// <summary>
    /// Una sesion inexistente y una ajena deben ser indistinguibles para quien
    /// pregunta: ambas devuelven false y el filtro responde el mismo 404.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IsSessionAccessibleAsync_ShouldBeFalse_WhenSessionDoesNotExist(bool isPrivileged)
    {
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        _experimentSessionRepository.GetByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns((ExperimentSession?)null);

        var accesible = await service.IsSessionAccessibleAsync(sessionId, Guid.NewGuid(), isPrivileged);

        accesible.Should().BeFalse();
    }

    private ExperimentSessionService CreateService() =>
        new(_experimentSessionRepository, Microsoft.Extensions.Options.Options.Create(new SessionOptions()));
}
