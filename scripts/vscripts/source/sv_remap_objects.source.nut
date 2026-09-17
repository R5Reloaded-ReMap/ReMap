/*
 * ReMap game scripts
 *
 * Source lineage: rewritten from the active R5Reloaded ReMap implementation
 * and the legacy ReMap scripts.
 * Original ReMap: Zee (@AyeZeeBB on X, zee_x64 on Discord)
 * and Julefox (@Julefox_ on X, julefox on Discord).
 * This standalone ReMap rewrite was created and is maintained by Julefox.
 *
 * Made with love for the Apex modding community. <3
 * Built on the Apex modding foundation created by Amos (@AmosModz on X, amosmodz on Discord):
 * https://github.com/Mauler125/r5sdk
 */

global function ReMap_ClearProps
global function ReMap_CreateProp
global function ReMap_CreateDoor

global const int REMAP_DOOR_SINGLE = 0
global const int REMAP_DOOR_DOUBLE = 1
global const int REMAP_DOOR_VERTICAL = 2
global const int REMAP_DOOR_HORIZONTAL = 3

const asset REMAP_DOOR_MODEL_SINGLE = $"mdl/door/canyonlands_door_single_02.rmdl"
const asset REMAP_DOOR_MODEL_VERTICAL = $"mdl/door/door_canyonlands_large_01_animated.rmdl"
const asset REMAP_DOOR_MODEL_HORIZONTAL = $"mdl/door/door_256x256x8_elevatorstyle02_animated.rmdl"

struct
{
	array< entity > props
} file

void function ReMap_ClearProps()
{
	foreach ( entity prop in file.props )
	{
		if ( IsValid( prop ) )
			prop.Destroy()
	}

	file.props.clear()
}

entity function ReMap_CreateProp( asset model, vector origin, vector angles, bool allowMantle = true, float fadeDistance = 50000.0, int realmId = -1, float scale = 1.0 )
{
	entity prop = CreatePropDynamic( model, origin, angles, SOLID_VPHYSICS, fadeDistance )
	prop.kv.fadedist = fadeDistance
	prop.kv.rendermode = 0
	prop.kv.renderamt = 1
	prop.kv.solid = 6
	prop.kv.CollisionGroup = TRACE_COLLISION_GROUP_PLAYER
	prop.SetScriptName( "remap_prop" )
	prop.SetModelScale( scale )

	if ( allowMantle )
		prop.AllowMantle()

	if ( realmId > -1 )
	{
		prop.RemoveFromAllRealms()
		prop.AddToRealm( realmId )
	}
#if R5R
	prop.e.gameModeId = realmId
#endif

	file.props.append( prop )
	return prop
}

void function ReMap_CreateDoor( vector origin, vector angles, int type = REMAP_DOOR_SINGLE, bool gold = false, bool spawnOpen = false )
{
	switch ( type )
	{
		case REMAP_DOOR_SINGLE:
		{
			entity door = ReMap_CreateDoorEntity( "prop_door", REMAP_DOOR_MODEL_SINGLE, "", origin, angles, gold )
			DispatchSpawn( door )
			if ( spawnOpen )
				ReMap_OpenCodeDoorAtSpawn( door )
			file.props.append( door )
			break
		}

		case REMAP_DOOR_DOUBLE:
		{
			entity door = ReMap_CreateDoorEntity( "prop_door", REMAP_DOOR_MODEL_SINGLE, "", origin, angles, gold )
			door.SetOrigin( origin + door.GetRightVector() * 60.0 )
			DispatchSpawn( door )

			entity oppositeDoor = ReMap_CreateDoorEntity( "prop_door", REMAP_DOOR_MODEL_SINGLE, "", origin,
				< -angles.x, angles.y + 180.0, -angles.z >, gold )
			oppositeDoor.SetOrigin( origin + oppositeDoor.GetRightVector() * 60.0 )
			oppositeDoor.LinkToEnt( door )
			DispatchSpawn( oppositeDoor )

			if ( spawnOpen )
				ReMap_OpenCodeDoorAtSpawn( door )
			file.props.append( door )
			file.props.append( oppositeDoor )
			break
		}

		case REMAP_DOOR_VERTICAL:
		{
			entity door = ReMap_CreateDoorEntity( "prop_dynamic", REMAP_DOOR_MODEL_VERTICAL,
				"survival_door_plain", origin, angles, false )
			DispatchSpawn( door )
			if ( spawnOpen )
				ReMap_OpenPlainDoorAtSpawn( door )
			file.props.append( door )
			break
		}

		case REMAP_DOOR_HORIZONTAL:
		{
			entity door = ReMap_CreateDoorEntity( "prop_dynamic", REMAP_DOOR_MODEL_HORIZONTAL,
				"survival_door_plain", origin, angles, false )
			DispatchSpawn( door )
			if ( spawnOpen )
				ReMap_OpenPlainDoorAtSpawn( door )
			file.props.append( door )
			break
		}
	}
}

entity function ReMap_CreateDoorEntity( string entityType, asset model, string scriptName,
	vector origin, vector angles, bool gold )
{
	entity door = CreateEntity( entityType )
	door.SetOrigin( origin )
	door.SetAngles( angles )
	door.SetValueForModelKey( model )

	if ( scriptName != "" )
	{
		door.SetScriptName( scriptName )
		door.kv.solid = 6
	}
	if ( gold )
		door.SetSkin( 1 )

	return door
}

void function ReMap_OpenCodeDoorAtSpawn( entity door )
{
	entity fakeUser = CreateEntity( "prop_dynamic" )
	fakeUser.SetOrigin( door.GetOrigin() - door.GetForwardVector() * 100.0 )
	door.OpenDoor( fakeUser )
	fakeUser.Destroy()
}

void function ReMap_OpenPlainDoorAtSpawn( entity door )
{
	PlayAnimNoWait( door, "open" )
	door.e.isOpen = true
	GradeFlagsSet( door, eGradeFlags.IS_OPEN )
	door.SetUsePrompts( "#SURVIVAL_CLOSE_DOOR", "#SURVIVAL_CLOSE_DOOR" )
}
