using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;

public class HexUnit : MonoBehaviour
{
    HexCell location, currentTravelLocation;

    float orientation;

    public static HexUnit unitPrefab;

    //HexUnit remember the path it's supposed to travel, so it can visualize it using gizmos.
    List<HexCell> pathToTravel;

    const float travelSpeed = 4f;

    const float rotationSpeed = 180f;

    public HexGrid Grid { get; set; }

    public int VisionRange
    {
        get
        {
            return 3;
        }
    }

    void OnEnable()
    {

        if (location)
        {
            transform.localPosition = location.Position;
            if (currentTravelLocation)
            {
                Grid.IncreaseVisibility(location, VisionRange);
                Grid.DecreaseVisibility(currentTravelLocation, VisionRange);
                currentTravelLocation = null;
            }
        }
    }

    //set the unit's position. Use the time delta instead of fixed 0.1 increments. 
    // //And yield each iteration. That will move the unit from one cell to the 
    // next in one second.
    IEnumerator TravelPath()
    {
        Vector3 a, b, c = pathToTravel[0].Position;
        yield return LookAt(pathToTravel[1].Position);
       /* Grid.DecreaseVisibility(
            currentTravelLocation ? currentTravelLocation : pathToTravel[0],
            visionRange
        );*/
        if (!currentTravelLocation) {
			currentTravelLocation = pathToTravel[0];
		}
		Grid.DecreaseVisibility(currentTravelLocation, VisionRange);
		int currentColumn = currentTravelLocation.ColumnIndex;

        float t = Time.deltaTime * travelSpeed;
        for (int i = 1; i < pathToTravel.Count; i++)
        {
            currentTravelLocation = pathToTravel[i];

            a = c;
            b = pathToTravel[i - 1].Position;
            //c = (b + currentTravelLocation.Position) * 0.5f;
            //Grid.IncreaseVisibility(pathToTravel[i], visionRange);
            
			int nextColumn = currentTravelLocation.ColumnIndex;
			if (currentColumn != nextColumn) {
                if (nextColumn < currentColumn - 1) {
					a.x -= HexMetrics.innerDiameter * HexMetrics.wrapSize;
					b.x -= HexMetrics.innerDiameter * HexMetrics.wrapSize;
				}
				else if (nextColumn > currentColumn + 1) {
					a.x += HexMetrics.innerDiameter * HexMetrics.wrapSize;
					b.x += HexMetrics.innerDiameter * HexMetrics.wrapSize;
				}
				Grid.MakeChildOfColumn(transform, nextColumn);
				currentColumn = nextColumn;
			}
            c = (b + currentTravelLocation.Position) * 0.5f;
			Grid.IncreaseVisibility(pathToTravel[i], VisionRange);

            for (; t < 1f; t += Time.deltaTime * travelSpeed)
            {
                transform.localPosition = Bezier.GetPoint(a, b, c, t);

                //derivative vector aligns with the travel direction. 
                //method to convert that into a rotation for our unit
                Vector3 d = Bezier.GetDerivative(a, b, c, t);
                d.y = 0f;
                transform.localRotation = Quaternion.LookRotation(d);
                yield return null;
            }
            Grid.DecreaseVisibility(pathToTravel[i], VisionRange);
            t -= 1f;
        }
        currentTravelLocation = null;

        a = c;
        b = location.Position;
        c = b;

        Grid.IncreaseVisibility(location, VisionRange);
        for (; t < 1f; t += Time.deltaTime * travelSpeed)
        {
            transform.localPosition = Bezier.GetPoint(a, b, c, t);
            Vector3 d = Bezier.GetDerivative(a, b, c, t);
            d.y = 0f;
            transform.localRotation = Quaternion.LookRotation(d);
            yield return null;
        }

        // make sure that the unit ends up exactly at its destination.
        transform.localPosition = location.Position;
        orientation = transform.localRotation.eulerAngles.y;

        //no longer need to remember which path we traveled
        // can release the cell list at the end
        ListPool<HexCell>.Add(pathToTravel);
        pathToTravel = null;
    }

    //so that Units identify the cell 
    // that they are occupying
    public HexCell Location
    {
        get
        {
            return location;
        }
        set
        {
            if (location)
            {
                Grid.DecreaseVisibility(location, VisionRange);
                location.Unit = null;
            }
            Grid.MakeChildOfColumn(transform, value.ColumnIndex);

            location = value;
            //make the cell aware that there is a unit standing on it.
            value.Unit = this;
            Grid.IncreaseVisibility(value, VisionRange);
            transform.localPosition = value.Position;
        }
    }

    //teleport Unit to the destination.
    public void Travel(List<HexCell> path)
    {
        location.Unit = null;
        location = path[path.Count - 1];
        location.Unit = this;

        pathToTravel = path;
        //stop all existing coroutines. 
        StopAllCoroutines();
        StartCoroutine(TravelPath());
    }

    //sets the hexunits orientation or allows it to be changed in how it is facing
    public float Orientation
    {
        get
        {
            return orientation;
        }
        set
        {
            orientation = value;
            transform.localRotation = Quaternion.Euler(0f, value, 0f);
        }
    }

    //validate the unit's location after a change has been made by elevation
    public void ValidateLocation()
    {
        transform.localPosition = location.Position;
    }

    // clearing the cell's unit reference
    public void Die()
    {
        if (location)
        {
            Grid.DecreaseVisibility(location, VisionRange);
        }
        location.Unit = null;
        Destroy(gameObject);
    }

    public void Save(BinaryWriter writer)
    {
        //write the unit's coordinates, and its orientation. 
        location.coordinates.Save(writer);
        writer.Write(orientation);
    }

    public static void Load(BinaryReader reader, HexGrid grid)
    {
        HexCoordinates coordinates = HexCoordinates.Load(reader);
        // reading the unit data. 
        float orientation = reader.ReadSingle();

        grid.AddUnit(
            Instantiate(unitPrefab), grid.GetCell(coordinates), orientation
        );
    }

    public bool IsValidDestination(HexCell cell)
    {
        return cell.IsExplored && !cell.IsUnderwater && !cell.Unit;
    }

    IEnumerator LookAt(Vector3 point)
    {
        if (HexMetrics.Wrapping) {
			float xDistance = point.x - transform.localPosition.x;
			if (xDistance < -HexMetrics.innerRadius * HexMetrics.wrapSize) {
				point.x += HexMetrics.innerDiameter * HexMetrics.wrapSize;
			}
			else if (xDistance > HexMetrics.innerRadius * HexMetrics.wrapSize) {
				point.x -= HexMetrics.innerDiameter * HexMetrics.wrapSize;
			}
		}
        point.y = transform.localPosition.y;

        Quaternion fromRotation = transform.localRotation;
        Quaternion toRotation =
            Quaternion.LookRotation(point - transform.localPosition);

        float angle = Quaternion.Angle(fromRotation, toRotation);

        if (angle > 0f)
        {
            float speed = rotationSpeed / angle;

            for (
                float t = Time.deltaTime * speed;
                t < 1f;
                t += Time.deltaTime * speed
            )
            {
                transform.localRotation =
                    Quaternion.Slerp(fromRotation, toRotation, t);
                yield return null;
            }
        }

        transform.LookAt(point);
        orientation = transform.localRotation.eulerAngles.y;
    }

    //to determine the move cost.
    //needs to know which cells the movement is between as well as the direction. 
    public int GetMoveCost(
        HexCell fromCell, HexCell toCell, HexDirection direction)
    {
        //cells skip cliffs
        HexEdgeType edgeType = fromCell.GetEdgeType(toCell);
        if (edgeType == HexEdgeType.Cliff)
        {
            return -1;
        }

        //make it easy and fast to travel by road
        //leave road at 1 ancrease cost of other edges to 10
        int moveCost;
        if (fromCell.HasRoadThroughEdge(direction))
        {
            moveCost = 1;
        }
        else if (fromCell.Walled != toCell.Walled)
        {
            return -1;
        }
        else
        {
            moveCost = edgeType == HexEdgeType.Flat ? 5 : 10;

            //cost for terrain features
            moveCost +=
                toCell.UrbanLevel + toCell.FarmLevel + toCell.PlantLevel;
        }
        return moveCost;
    }

    public int Speed
    {
        get
        {
            return 24;
        }
    }

    
}