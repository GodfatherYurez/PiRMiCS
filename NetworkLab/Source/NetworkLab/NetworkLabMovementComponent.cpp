// Copyright Epic Games, Inc. All Rights Reserved.

#include "NetworkLabMovementComponent.h"
#include "Interfaces/MovementBaseInterface.h"

#include "NetworkLabCharacter.h"

void FNetworkLabSavedMove::Clear()
{
	Super::Clear();
	bSavedWantsToSprint = false;
	bSavedUsesPrediction = true;
}

uint8 FNetworkLabSavedMove::GetCompressedFlags() const
{
	uint8 Result = Super::GetCompressedFlags();
	if (bSavedWantsToSprint)
	{
		Result |= FLAG_Custom_0;
	}
	if (bSavedUsesPrediction)
	{
		Result |= FLAG_Custom_1;
	}
	return Result;
}

bool FNetworkLabSavedMove::CanCombineWith(const FSavedMovePtr& NewMove, ACharacter* Character, float MaxDelta) const
{
	const FNetworkLabSavedMove* OtherMove = static_cast<const FNetworkLabSavedMove*>(NewMove.Get());
	if (bSavedWantsToSprint != OtherMove->bSavedWantsToSprint || bSavedUsesPrediction != OtherMove->bSavedUsesPrediction)
	{
		return false;
	}
	return Super::CanCombineWith(NewMove, Character, MaxDelta);
}

void FNetworkLabSavedMove::SetMoveFor(ACharacter* Character, float InDeltaTime, FVector const& NewAcceleration, FNetworkPredictionData_Client_Character& ClientData)
{
	Super::SetMoveFor(Character, InDeltaTime, NewAcceleration, ClientData);
	if (const ANetworkLabCharacter* NetworkCharacter = Cast<ANetworkLabCharacter>(Character))
	{
		const UNetworkLabMovementComponent* Movement = Cast<UNetworkLabMovementComponent>(NetworkCharacter->GetCharacterMovement());
		if (Movement)
		{
			bSavedWantsToSprint = Movement->bWantsToSprint;
			bSavedUsesPrediction = Movement->bUsesClientPrediction;
		}
	}
}

void FNetworkLabSavedMove::PrepMoveFor(ACharacter* Character)
{
	Super::PrepMoveFor(Character);
	if (ANetworkLabCharacter* NetworkCharacter = Cast<ANetworkLabCharacter>(Character))
	{
		if (UNetworkLabMovementComponent* Movement = Cast<UNetworkLabMovementComponent>(NetworkCharacter->GetCharacterMovement()))
		{
			Movement->bWantsToSprint = bSavedWantsToSprint;
			Movement->bUsesClientPrediction = bSavedUsesPrediction;
		}
	}
}

UNetworkLabMovementComponent::UNetworkLabMovementComponent()
{
	WalkSpeed = 500.0f;
	SprintSpeed = 900.0f;
}

void UNetworkLabMovementComponent::SetSprintIntent(bool bWants, bool bUseClientPrediction)
{
	bUsesClientPrediction = bUseClientPrediction;
	bWantsToSprint = bWants && IsMovingOnGround();
}

void UNetworkLabMovementComponent::SetServerSprintAuthorization(bool bAuthorized)
{
	bServerSprintAuthorized = bAuthorized;
}

void UNetworkLabMovementComponent::SetPredictionTestSpeedBoost(bool bEnabled)
{
	PredictionTestSpeedMultiplier = bEnabled ? 1.8f : 1.0f;
}

float UNetworkLabMovementComponent::GetMaxSpeed() const
{
	if (bWantsToSprint && IsMovingOnGround() && !IsCrouching())
	{
		return SprintSpeed * PredictionTestSpeedMultiplier;
	}
	return WalkSpeed;
}

FNetworkPredictionData_Client* UNetworkLabMovementComponent::GetPredictionData_Client() const
{
	check(PawnOwner != nullptr);
	if (!ClientPredictionData)
	{
		UNetworkLabMovementComponent* MutableThis = const_cast<UNetworkLabMovementComponent*>(this);
		MutableThis->ClientPredictionData = new FNetworkLabNetworkPredictionData_Client(*this);
	}
	return ClientPredictionData;
}

void UNetworkLabMovementComponent::UpdateFromCompressedFlags(uint8 Flags)
{
	Super::UpdateFromCompressedFlags(Flags);
	const bool bSprintRequested = (Flags & FSavedMove_Character::FLAG_Custom_0) != 0;
	bUsesClientPrediction = (Flags & FSavedMove_Character::FLAG_Custom_1) != 0;
	bWantsToSprint = bSprintRequested && IsMovingOnGround() && (bUsesClientPrediction || bServerSprintAuthorized);
	if (!bSprintRequested)
	{
		bServerSprintAuthorized = false;
	}
}

void UNetworkLabMovementComponent::OnClientCorrectionReceived(FNetworkPredictionData_Client_Character& ClientData, float TimeStamp, FVector NewLocation, FVector NewVelocity, FMovementBaseInterfaceData* NewMovementBaseInterfaceData, FName NewBaseBoneName, bool bHasBase, bool bBaseRelativePosition, uint8 ServerMovementMode, FVector ServerGravityDirection)
{
	UE_LOG(LogTemp, Display, TEXT("[Prediction] Server correction received at %.2fs; location %s"), TimeStamp, *NewLocation.ToCompactString());
	Super::OnClientCorrectionReceived(ClientData, TimeStamp, NewLocation, NewVelocity, NewMovementBaseInterfaceData, NewBaseBoneName, bHasBase, bBaseRelativePosition, ServerMovementMode, ServerGravityDirection);
}

FNetworkLabNetworkPredictionData_Client::FNetworkLabNetworkPredictionData_Client(const UCharacterMovementComponent& ClientMovement)
	: Super(ClientMovement)
{
}

FSavedMovePtr FNetworkLabNetworkPredictionData_Client::AllocateNewMove()
{
	return FSavedMovePtr(new FNetworkLabSavedMove());
}
