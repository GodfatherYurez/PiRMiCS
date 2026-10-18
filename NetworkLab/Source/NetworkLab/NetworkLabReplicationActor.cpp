// Copyright Epic Games, Inc. All Rights Reserved.

#include "NetworkLabReplicationActor.h"

#include "Components/StaticMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "Net/UnrealNetwork.h"
#include "UObject/ConstructorHelpers.h"

ANetworkLabReplicationActor::ANetworkLabReplicationActor()
{
	PrimaryActorTick.bCanEverTick = true;
	PrimaryActorTick.bStartWithTickEnabled = true;

	MarkerMesh = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("MarkerMesh"));
	SetRootComponent(MarkerMesh);
	MarkerMesh->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	MarkerMesh->SetGenerateOverlapEvents(false);
	MarkerMesh->SetRelativeScale3D(FVector(0.22f));

	static ConstructorHelpers::FObjectFinder<UStaticMesh> CubeMesh(TEXT("/Engine/BasicShapes/Cube.Cube"));
	if (CubeMesh.Succeeded())
	{
		MarkerMesh->SetStaticMesh(CubeMesh.Object);
	}

	bReplicates = true;
	bAlwaysRelevant = true;
	SetReplicateMovement(false);
	SetNetUpdateFrequency(30.0f);
	SetMinNetUpdateFrequency(2.0f);
}

void ANetworkLabReplicationActor::Configure(bool bInStatic, int32 InIndex)
{
	bStaticMarker = bInStatic;
	MarkerIndex = InIndex;
	SetActorLabel(FString::Printf(TEXT("Network marker %02d (%s)"), InIndex, bStaticMarker ? TEXT("static") : TEXT("moving")));
}

void ANetworkLabReplicationActor::BeginPlay()
{
	Super::BeginPlay();
	BaseLocation = GetActorLocation();
	ProfilePosition = BaseLocation;
	UE_LOG(LogTemp, Display, TEXT("[ReplicationLab] Marker %02d spawned (%s, %s)"), MarkerIndex, bStaticMarker ? TEXT("static") : TEXT("moving"), HasAuthority() ? TEXT("server") : TEXT("client"));
}

void ANetworkLabReplicationActor::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (!HasAuthority())
	{
		return;
	}

	TimeSincePulse += DeltaSeconds;
	if (bStaticMarker)
	{
		if (!bOptimizationEnabled && TimeSincePulse >= 0.5f)
		{
			TimeSincePulse = 0.0f;
			++ProfilePulse;
		}
		return;
	}

	const float Wave = FMath::Sin(GetGameTimeSinceCreation() * 1.5f + static_cast<float>(MarkerIndex)) * 24.0f;
	ProfilePosition = BaseLocation + FVector(0.0f, 0.0f, Wave);
	if (TimeSincePulse >= 0.5f)
	{
		TimeSincePulse = 0.0f;
		++ProfilePulse;
	}
}

void ANetworkLabReplicationActor::SetOptimizationEnabled(bool bEnabled)
{
	if (bOptimizationEnabled == bEnabled)
	{
		return;
	}

	bOptimizationEnabled = bEnabled;
	if (bOptimizationEnabled)
	{
		SetNetUpdateFrequency(bStaticMarker ? 30.0f : 5.0f);
		if (bStaticMarker)
		{
			FlushNetDormancy();
			SetNetDormancy(DORM_DormantAll);
		}
	}
	else
	{
		SetNetDormancy(DORM_Awake);
		SetNetUpdateFrequency(30.0f);
		ForceNetUpdate();
	}
	UE_LOG(LogTemp, Display, TEXT("[ReplicationLab] Marker %02d optimization %s, NetUpdateFrequency=%.0f, dormancy=%d"), MarkerIndex, bOptimizationEnabled ? TEXT("enabled") : TEXT("disabled"), GetNetUpdateFrequency(), static_cast<int32>(NetDormancy));
}

void ANetworkLabReplicationActor::OnRep_ProfilePosition()
{
	SetActorLocation(ProfilePosition);
}

void ANetworkLabReplicationActor::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME(ANetworkLabReplicationActor, ProfilePosition);
	DOREPLIFETIME(ANetworkLabReplicationActor, ProfilePulse);
}
