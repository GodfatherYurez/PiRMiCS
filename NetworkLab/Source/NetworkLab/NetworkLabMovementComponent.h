// Copyright Epic Games, Inc. All Rights Reserved.

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "NetworkLabMovementComponent.generated.h"

class ANetworkLabCharacter;
struct FMovementBaseInterfaceData;

/** Stores the sprint button alongside each predicted movement step. */
class FNetworkLabSavedMove final : public FSavedMove_Character
{
public:
	using Super = FSavedMove_Character;

	bool bSavedWantsToSprint = false;
	bool bSavedUsesPrediction = true;

	virtual void Clear() override;
	virtual uint8 GetCompressedFlags() const override;
	virtual bool CanCombineWith(const FSavedMovePtr& NewMove, ACharacter* Character, float MaxDelta) const override;
	virtual void SetMoveFor(ACharacter* Character, float DeltaTime, FVector const& NewAcceleration, FNetworkPredictionData_Client_Character& ClientData) override;
	virtual void PrepMoveFor(ACharacter* Character) override;
};

/** CharacterMovementComponent extension for server-validated sprint prediction. */
UCLASS()
class UNetworkLabMovementComponent : public UCharacterMovementComponent
{
	GENERATED_BODY()

public:
	UNetworkLabMovementComponent();

	void SetSprintIntent(bool bWantsToSprint, bool bUseClientPrediction);
	bool IsSprintIntentActive() const { return bWantsToSprint; }
	void SetServerSprintAuthorization(bool bAuthorized);
	void SetPredictionTestSpeedBoost(bool bEnabled);

	virtual float GetMaxSpeed() const override;
	virtual FNetworkPredictionData_Client* GetPredictionData_Client() const override;
	virtual void UpdateFromCompressedFlags(uint8 Flags) override;

protected:
	virtual void OnClientCorrectionReceived(FNetworkPredictionData_Client_Character& ClientData, float TimeStamp, FVector NewLocation, FVector NewVelocity, FMovementBaseInterfaceData* NewMovementBaseInterfaceData, FName NewBaseBoneName, bool bHasBase, bool bBaseRelativePosition, uint8 ServerMovementMode, FVector ServerGravityDirection) override;

private:
	friend class FNetworkLabSavedMove;

	bool bWantsToSprint = false;
	bool bUsesClientPrediction = true;
	bool bServerSprintAuthorized = false;
	float WalkSpeed = 500.0f;
	float SprintSpeed = 900.0f;
	float PredictionTestSpeedMultiplier = 1.0f;
};

class FNetworkLabNetworkPredictionData_Client final : public FNetworkPredictionData_Client_Character
{
public:
	using Super = FNetworkPredictionData_Client_Character;
	explicit FNetworkLabNetworkPredictionData_Client(const UCharacterMovementComponent& ClientMovement);
	virtual FSavedMovePtr AllocateNewMove() override;
};
