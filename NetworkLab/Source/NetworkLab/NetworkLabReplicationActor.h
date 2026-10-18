// Copyright Epic Games, Inc. All Rights Reserved.

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "NetworkLabReplicationActor.generated.h"

class UStaticMeshComponent;

/** A visible replicated marker used to profile actor and property traffic. */
UCLASS()
class ANetworkLabReplicationActor : public AActor
{
	GENERATED_BODY()

public:
	ANetworkLabReplicationActor();
	void Configure(bool bInStatic, int32 InIndex);
	void SetOptimizationEnabled(bool bEnabled);

protected:
	virtual void BeginPlay() override;
	virtual void Tick(float DeltaSeconds) override;
	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;

private:
	UFUNCTION()
	void OnRep_ProfilePosition();

	UPROPERTY(VisibleAnywhere)
	TObjectPtr<UStaticMeshComponent> MarkerMesh;

	UPROPERTY(ReplicatedUsing=OnRep_ProfilePosition)
	FVector_NetQuantize10 ProfilePosition;

	UPROPERTY(Replicated)
	uint8 ProfilePulse = 0;

	bool bStaticMarker = false;
	bool bOptimizationEnabled = false;
	int32 MarkerIndex = 0;
	float TimeSincePulse = 0.0f;
	FVector BaseLocation = FVector::ZeroVector;
};
