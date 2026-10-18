// Copyright Epic Games, Inc. All Rights Reserved.

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "NetworkLabGameMode.generated.h"

class ANetworkLabReplicationActor;

/**
 *  Simple GameMode for a third person game
 */
UCLASS(abstract)
class ANetworkLabGameMode : public AGameModeBase
{
	GENERATED_BODY()

public:
	
	/** Constructor */
	ANetworkLabGameMode();
	virtual void BeginPlay() override;
	virtual void Tick(float DeltaSeconds) override;

	UFUNCTION(BlueprintCallable, Category="Replication Lab")
	void ToggleReplicationOptimization();

private:
	TArray<TObjectPtr<ANetworkLabReplicationActor>> ReplicationMarkers;
	bool bReplicationOptimizationEnabled = false;
	bool bCaptureStarted = false;
	bool bCaptureFinished = false;
	float CaptureElapsedSeconds = 0.0f;
	uint64 OutgoingClientBytesAtCaptureStart = 0;
};



