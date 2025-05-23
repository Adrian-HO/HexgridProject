using UnityEngine;
using UnityEngine.UI;

public class HexGridChunk : MonoBehaviour
{
    public HexMesh terrain, rivers, roads, water, waterShore, estuaries;

    public HexFeatureManager features;

    HexCell[] cells;

    Canvas gridCanvas;

    static Color weights1 = new Color(1f, 0f, 0f);
    static Color weights2 = new Color(0f, 1f, 0f);
    static Color weights3 = new Color(0f, 0f, 1f);


    void Awake()
    {
        gridCanvas = GetComponentInChildren<Canvas>();

        cells = new HexCell[HexMetrics.chunkSizeX * HexMetrics.chunkSizeZ];

        //changed to initially set to true cause otherwise grid just wont show
        //ShowUI(false);
    }

    public void AddCell(int index, HexCell cell)
    {
        cells[index] = cell;
        cell.chunk = this;
        cell.transform.SetParent(transform, false);
        cell.uiRect.SetParent(gridCanvas.transform, false);
    }

    public void Refresh()
    {
        enabled = true;
    }

    public void ShowUI(bool visible)
    {
        //inverted cause it was causing me confusion
        gridCanvas.gameObject.SetActive(visible);
    }

    //diff between update and late update
    //Each frame, the Update methods of enabled components are invoked at some point, 
    //in arbitrary order. After that's finished, the same happens with LateUpdate methods. 
    //So there are two update steps, an early and a late one.
    void LateUpdate()
    {
        Triangulate();
        enabled = false;
    }

    public void Triangulate()
    {
        terrain.Clear();
        rivers.Clear();
        roads.Clear();
        water.Clear();
        waterShore.Clear();
        estuaries.Clear();
        features.Clear();

        for (int i = 0; i < cells.Length; i++)
        {
            Triangulate(cells[i]);
        }

        terrain.Apply();
        rivers.Apply();
        roads.Apply();
        water.Apply();
        waterShore.Apply();
        estuaries.Apply();
        features.Apply();
    }

    void Triangulate(HexCell cell)
    {
        if (cell)
        {
            for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
            {
                Triangulate(d, cell);
            }

            //make sure that a cell is clear before we add a feature to it
            if (!cell.IsUnderwater)
            {
                if (!cell.HasRiver && !cell.HasRoads)
                {
                    //a single feature in the center of every cell.
                    features.AddFeature(cell, cell.Position);
                }

                if (cell.IsSpecial)
                {
                    features.AddSpecialFeature(cell, cell.Position);
                }
            }
        }
    }

    //using direction to identify the parts
    void Triangulate(HexDirection direction, HexCell cell)
    {
        //null error fix
        if (cell)
        {
            Vector3 center = cell.Position;
            EdgeVertices e = new EdgeVertices(
                center + HexMetrics.GetFirstSolidCorner(direction),
                center + HexMetrics.GetSecondSolidCorner(direction)
            );

            if (cell.HasRiver) //if river triangulate triangle into a channel
            {
                //detect whether there is a river flowing through its edge
                if (cell.HasRiverThroughEdge(direction))
                {
                    //drop the middle edge vertex to the stream bed's height.
                    e.v3.y = cell.StreamBedY;

                    //Triangulating a part that has only the beginning or end of a river is 
                    //different enough that it warrants its own method. So check for it in 
                    //Triangulate and invoke the appropriate method.
                    if (cell.HasRiverBeginOrEnd)
                    {
                        TriangulateWithRiverBeginOrEnd(direction, cell, center, e);
                    }
                    else
                    {
                        TriangulateWithRiver(direction, cell, center, e);
                    }
                }
                else
                { //when the cell has a river, but it doesn't flow through the current direction.
                    TriangulateAdjacentToRiver(direction, cell, center, e);
                }

            }
            else //else keep using a triangle fan
            {
                TriangulateWithoutRiver(direction, cell, center, e);

                //add an addition feature to the center of each of a cell's six 
                //triangles. 
                if (!cell.IsUnderwater && !cell.HasRoadThroughEdge(direction))
                {
                    features.AddFeature(cell, (center + e.v1 + e.v5) * (1f / 3f));
                }
            }

            if (direction <= HexDirection.SE)
            {
                TriangulateConnection(direction, cell, e);
            }

            if (cell.IsUnderwater) //water forms a second layer,
            {
                if (cell)
                {
                    TriangulateWater(direction, cell, center);
                }
            }
        }
    }

    void TriangulateWater(
        HexDirection direction, HexCell cell, Vector3 center
    )
    {

        center.y = cell.WaterSurfaceY;

        HexCell neighbor = cell.GetNeighbor(direction);
        if (neighbor != null && !neighbor.IsUnderwater)
        {
            TriangulateWaterShore(direction, cell, neighbor, center);
        }
        else
        {
            TriangulateOpenWater(direction, cell, neighbor, center);
        }
    }

    void TriangulateOpenWater(
        HexDirection direction, HexCell cell, HexCell neighbor, Vector3 center
    )
    {
        Vector3 c1 = center + HexMetrics.GetFirstWaterCorner(direction);
        Vector3 c2 = center + HexMetrics.GetSecondWaterCorner(direction);

        water.AddTriangle(center, c1, c2);

        Vector3 indices;
        indices.x = indices.y = indices.z = cell.Index;
        water.AddTriangleCellData(indices, weights1);

        //connect adjacent watercells with a single quad
        if (direction <= HexDirection.SE && neighbor != null)
        {
            Vector3 bridge = HexMetrics.GetWaterBridge(direction);
            Vector3 e1 = c1 + bridge;
            Vector3 e2 = c2 + bridge;

            water.AddQuad(c1, c2, e1, e2);

            indices.y = neighbor.Index;
            water.AddQuadCellData(indices, weights1, weights2);

            // fill the corners with a single triangle.
            if (direction <= HexDirection.E)
            {
                HexCell nextNeighbor = cell.GetNeighbor(direction.Next());
                if (nextNeighbor == null || !nextNeighbor.IsUnderwater)
                {
                    return;
                }
                water.AddTriangle(
                    c2, e2, c2 + HexMetrics.GetWaterBridge(direction.Next())
                );

                indices.z = nextNeighbor.Index;
                water.AddTriangleCellData(
                    indices, weights1, weights2, weights3
                );
            }
        }
    }

    void TriangulateWaterShore(
        HexDirection direction, HexCell cell, HexCell neighbor, Vector3 center
    )
    {
        //perturb the water triangles along the shore as well. 
        //So we need edge vertices and a triangle fan.
        EdgeVertices e1 = new EdgeVertices(
            center + HexMetrics.GetFirstWaterCorner(direction),
            center + HexMetrics.GetSecondWaterCorner(direction)
        );
        water.AddTriangle(center, e1.v1, e1.v2);
        water.AddTriangle(center, e1.v2, e1.v3);
        water.AddTriangle(center, e1.v3, e1.v4);
        water.AddTriangle(center, e1.v4, e1.v5);

        Vector3 indices;
        indices.x = indices.z = cell.Index;
        indices.y = neighbor.Index;
        water.AddTriangleCellData(indices, weights1);
        water.AddTriangleCellData(indices, weights1);
        water.AddTriangleCellData(indices, weights1);
        water.AddTriangleCellData(indices, weights1);

        Vector3 center2 = neighbor.Position;
        if (neighbor.ColumnIndex < cell.ColumnIndex - 1)
        {
            center2.x += HexMetrics.wrapSize * HexMetrics.innerDiameter;
        }
        else if (neighbor.ColumnIndex > cell.ColumnIndex + 1)
        {
            center2.x -= HexMetrics.wrapSize * HexMetrics.innerDiameter;
        }
        center2.y = center.y;
        EdgeVertices e2 = new EdgeVertices(
            center2 + HexMetrics.GetSecondSolidCorner(direction.Opposite()),
            center2 + HexMetrics.GetFirstSolidCorner(direction.Opposite())
        );

        if (cell.HasRiverThroughEdge(direction))
        {
            TriangulateEstuary(
                e1, e2, cell.IncomingRiver == direction, indices
            );
        }
        else
        {
            waterShore.AddQuad(e1.v1, e1.v2, e2.v1, e2.v2);
            waterShore.AddQuad(e1.v2, e1.v3, e2.v2, e2.v3);
            waterShore.AddQuad(e1.v3, e1.v4, e2.v3, e2.v4);
            waterShore.AddQuad(e1.v4, e1.v5, e2.v4, e2.v5);

            //info about the shore in the V coordinate
            //0 on the water side and to 1 on the land side
            waterShore.AddQuadUV(0f, 0f, 0f, 1f);
            waterShore.AddQuadCellData(indices, weights1, weights2);
            waterShore.AddQuadCellData(indices, weights1, weights2);
            waterShore.AddQuadCellData(indices, weights1, weights2);
            waterShore.AddQuadCellData(indices, weights1, weights2);
        }

        HexCell nextNeighbor = cell.GetNeighbor(direction.Next());
        if (nextNeighbor != null)
        {
            Vector3 center3 = nextNeighbor.Position;
            if (nextNeighbor.ColumnIndex < cell.ColumnIndex - 1)
            {
                center3.x += HexMetrics.wrapSize * HexMetrics.innerDiameter;
            }
            else if (nextNeighbor.ColumnIndex > cell.ColumnIndex + 1)
            {
                center3.x -= HexMetrics.wrapSize * HexMetrics.innerDiameter;
            }
            Vector3 v3 = center3 + (nextNeighbor.IsUnderwater ?
                HexMetrics.GetFirstWaterCorner(direction.Previous()) :
                HexMetrics.GetFirstSolidCorner(direction.Previous()));
            v3.y = center.y;
            waterShore.AddTriangle(e1.v5, e2.v5, v3);
            waterShore.AddTriangleUV(
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(0f, nextNeighbor.IsUnderwater ? 0f : 1f)
            );

            indices.z = nextNeighbor.Index;
            waterShore.AddTriangleCellData(
                indices, weights1, weights2, weights3
            );
        }
    }

    void TriangulateEstuary(
        EdgeVertices e1, EdgeVertices e2, bool incomingRiver, Vector3 indices
    )

    {
        //Make trapezoid shape to blend regions
        waterShore.AddTriangle(e2.v1, e1.v2, e1.v1);
        waterShore.AddTriangle(e2.v5, e1.v5, e1.v4);

        //use two water shore triangles at the sides.
        waterShore.AddTriangleUV(
            new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(0f, 0f)
        );
        waterShore.AddTriangleUV(
            new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(0f, 0f)
        );

        waterShore.AddTriangleCellData(indices, weights2, weights1, weights1);
        waterShore.AddTriangleCellData(indices, weights2, weights1, weights1);

        estuaries.AddQuad(e2.v1, e1.v2, e2.v2, e1.v3);
        estuaries.AddTriangle(e1.v3, e2.v2, e2.v4);
        estuaries.AddQuad(e1.v3, e1.v4, e2.v4, e2.v5);

        estuaries.AddQuadUV(
            new Vector2(0f, 1f), new Vector2(0f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 0f)
        );
        estuaries.AddTriangleUV(
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(1f, 1f)
        );

        estuaries.AddQuadUV(
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 1f)
        );

        estuaries.AddQuadCellData(
            indices, weights2, weights1, weights2, weights1
        );
        estuaries.AddTriangleCellData(indices, weights1, weights2, weights2);
        estuaries.AddQuadCellData(indices, weights1, weights2);

        if (incomingRiver)
        {
            estuaries.AddQuadUV2(
                new Vector2(1.5f, 1f), new Vector2(0.7f, 1.15f),
                new Vector2(1f, 0.8f), new Vector2(0.5f, 1.1f)
            );

            estuaries.AddTriangleUV2(
                new Vector2(0.5f, 1.1f),
                new Vector2(1f, 0.8f),
                new Vector2(0f, 0.8f)
            );

            estuaries.AddQuadUV2(
                new Vector2(0.5f, 1.1f), new Vector2(0.3f, 1.15f),
                new Vector2(0f, 0.8f), new Vector2(-0.5f, 1f)
            );
        }
        else
        {
            estuaries.AddQuadUV2(
                new Vector2(-0.5f, -0.2f), new Vector2(0.3f, -0.35f),
                new Vector2(0f, 0f), new Vector2(0.5f, -0.3f)
            );
            estuaries.AddTriangleUV2(
                new Vector2(0.5f, -0.3f),
                new Vector2(0f, 0f),
                new Vector2(1f, 0f)
            );
            estuaries.AddQuadUV2(
                new Vector2(0.5f, -0.3f), new Vector2(0.7f, -0.35f),
                new Vector2(1f, 0f), new Vector2(1.5f, -0.2f)
            );
        }
    }

    void TriangulateWithoutRiver(
        HexDirection direction, HexCell cell, Vector3 center, EdgeVertices e
    )
    {
        //created an edge fan and set color of non river cells
        TriangulateEdgeFan(center, e, cell.Index);

        //add an invocation of TriangulateRoad, when there's actually a road. 
        //The left and right middle vertices can be found by interpolating between the center and the two corner vertices.
        if (cell.HasRoads)
        {
            //determine which interpolators to use. This will smooth out the roads.
            Vector2 interpolators = GetRoadInterpolators(direction, cell);
            TriangulateRoad(
                center,
                Vector3.Lerp(center, e.v1, interpolators.x),
                Vector3.Lerp(center, e.v5, interpolators.y),
                e, cell.HasRoadThroughEdge(direction), cell.Index
            );
        }
    }

    //terminate the channel at the center
    void TriangulateWithRiverBeginOrEnd(
        HexDirection direction, HexCell cell, Vector3 center, EdgeVertices e
    )
    {
        //create a middle edge between the center and outer edge. 
        EdgeVertices m = new EdgeVertices(
            Vector3.Lerp(center, e.v1, 0.5f),
            Vector3.Lerp(center, e.v5, 0.5f)
        );

        // Adjust the middle vertex to match the streambed height
        m.v3.y = e.v3.y;
        // Triangulate geometry
        TriangulateEdgeStrip( // Riverbanks
            m, weights1, cell.Index,
            e, weights1, cell.Index
        );
        TriangulateEdgeFan(center, e, cell.Index);         // Terminating fan

        // only add river segments when the current cell in not underwater.
        if (!cell.IsUnderwater)
        {
            // check whether we have an incoming river, to determine the flow direction. 
            //Then we can insert another river quad between the middle and edge.
            bool reversed = cell.HasIncomingRiver;
            Vector3 indices;
            indices.x = indices.y = indices.z = cell.Index;
            TriangulateRiverQuad(
                m.v2, m.v4, e.v2, e.v4,
                cell.RiverSurfaceY, 0.6f, reversed, indices
            );

            //The part between the center and middle is a triangle, so we cannot use TriangulateRiverQuad. 
            //The only significant difference is that the center vertex sits in the middle of the river. 
            //So its U coordinate is always ½.
            center.y = m.v2.y = m.v4.y = cell.RiverSurfaceY;
            rivers.AddTriangle(center, m.v2, m.v4);
            if (reversed)
            {
                rivers.AddTriangleUV(
                    new Vector2(0.5f, 0.4f),
                    new Vector2(1f, 0.2f), new Vector2(0f, 0.2f)
                );
            }
            else
            {
                rivers.AddTriangleCellData(indices, weights1);
            }
        }
    }

    //to create a channel straight across the cell part
    void TriangulateWithRiver(
            HexDirection direction, HexCell cell, Vector3 center, EdgeVertices e
        )
    {
        //If the cell has a river going through the opposite direction as well as 
        //the direction that we're working with, then it must be a straight river.
        Vector3 centerL, centerR;
        if (cell.HasRiverThroughEdge(direction.Opposite()))
        {
            //stretch centre into a line. line need to have same width as channel
            //find the left vertex by moving ¼ of the way from the center to the 
            //first corner of the previous part.
            centerL = center +
               HexMetrics.GetFirstSolidCorner(direction.Previous()) * 0.25f;

            //for the right vertex. In this case, we need the second corner of the next part.
            centerR = center +
               HexMetrics.GetSecondSolidCorner(direction.Next()) * 0.25f;
        }
        else if (cell.HasRiverThroughEdge(direction.Next()))
        {
            centerL = center;
            centerR = Vector3.Lerp(center, e.v5, 2f / 3f);
        }
        else if (cell.HasRiverThroughEdge(direction.Previous()))
        {
            centerL = Vector3.Lerp(center, e.v1, 2f / 3f);
            centerR = center;
        }
        //detect the direction of our curving river
        else if (cell.HasRiverThroughEdge(direction.Next2()))
        {
            centerL = center;
            centerR = center +
                HexMetrics.GetSolidEdgeMiddle(direction.Next()) *
                (0.5f * HexMetrics.innerToOuter);
        }
        else
        { //Otherwise, let's revert back to a single point by collapsing the center line.
            centerL = center +
                HexMetrics.GetSolidEdgeMiddle(direction.Previous()) *
                (0.5f * HexMetrics.innerToOuter);
            centerR = center;

        }

        //determine the final center by averaging them.
        center = Vector3.Lerp(centerL, centerR, 0.5f);

        //middle line can be found by creating edge vertices between the center and edge.
        EdgeVertices m = new EdgeVertices(
            Vector3.Lerp(centerL, e.v1, 0.5f),
            Vector3.Lerp(centerR, e.v5, 0.5f),
            1f / 6f
        );

        //adjust the middle vertex of the middle edge, 
        //as well as the center, so they become channel bottoms.
        m.v3.y = center.y = e.v3.y;

        //use TriangulateEdgeStrip to fill the space between the middle and edge lines.
        TriangulateEdgeStrip(
            m, weights1, cell.Index,
            e, weights1, cell.Index
        );

        terrain.AddTriangle(centerL, m.v1, m.v2);
        terrain.AddQuad(centerL, center, m.v2, m.v3);
        terrain.AddQuad(center, centerR, m.v3, m.v4);
        terrain.AddTriangle(centerR, m.v4, m.v5);

        Vector3 indices;
        indices.x = indices.y = indices.z = cell.Index;
        terrain.AddTriangleCellData(indices, weights1);
        terrain.AddQuadCellData(indices, weights1);
        terrain.AddQuadCellData(indices, weights1);
        terrain.AddTriangleCellData(indices, weights1);

        //show river if not underwater
        if (!cell.IsUnderwater)
        {
            //reverse direction when dealing with incoming river
            bool reversed = cell.IncomingRiver == direction;
            TriangulateRiverQuad(
                centerL, centerR, m.v2, m.v4,
                cell.RiverSurfaceY, 0.4f, reversed, indices
            );
            TriangulateRiverQuad(
                m.v2, m.v4, e.v2, e.v4,
                cell.RiverSurfaceY, 0.6f, reversed, indices
            );
        }
    }

    void TriangulateConnection
    (
        HexDirection direction, HexCell cell, EdgeVertices e1
    )
    {
        HexCell neighbor = cell.GetNeighbor(direction);
        if (neighbor == null)
        {
            return;
        }

        Vector3 bridge = HexMetrics.GetBridge(direction);
        bridge.y = neighbor.Position.y - cell.Position.y;
        EdgeVertices e2 = new EdgeVertices(
            e1.v1 + bridge,
            e1.v5 + bridge
        );

        bool hasRiver = cell.HasRiverThroughEdge(direction);
        bool hasRoad = cell.HasRoadThroughEdge(direction);
        //close holes that develop in terrain when triangulating connection
        if (hasRiver)
        {
            e2.v3.y = neighbor.StreamBedY;

            Vector3 indices;
            indices.x = indices.z = cell.Index;
            indices.y = neighbor.Index;

            //adding the river segment when neither the 
            //current nor the neighbor cell is underwater.
            if (!cell.IsUnderwater)
            {
                if (!neighbor.IsUnderwater)
                {
                    TriangulateRiverQuad(
                        e1.v2, e1.v4, e2.v2, e2.v4,
                        cell.RiverSurfaceY, neighbor.RiverSurfaceY, 0.8f,
                        cell.HasIncomingRiver && cell.IncomingRiver == direction,
                        indices
                    );
                }//when the neighbor ends up underwater and we have a waterfall.
                else if (cell.Elevation > neighbor.WaterLevel)
                {
                    TriangulateWaterfallInWater(
                        e1.v2, e1.v4, e2.v2, e2.v4,
                        cell.RiverSurfaceY, neighbor.RiverSurfaceY,
                        neighbor.WaterSurfaceY, indices
                    );
                }

            } //waterfalls in the opposite direction, when the current cell is underwater and the neighbor isn't.
            else if (
                !neighbor.IsUnderwater &&
                neighbor.Elevation > cell.WaterLevel
            )
            {
                TriangulateWaterfallInWater(
                    e2.v4, e2.v2, e1.v4, e1.v2,
                    neighbor.RiverSurfaceY, cell.RiverSurfaceY,
                    cell.WaterSurfaceY, indices
                );
            }
        }

        //decide whether to insert terraces or not.
        if (cell.GetEdgeType(direction) == HexEdgeType.Slope)
        {
            TriangulateEdgeTerraces(e1, cell, e2, neighbor, hasRoad);
        }
        else
        {
            //take care of the flats and cliffs.
            //TriangulateEdgeStrip(e1, cell.Color, e2, neighbor.Color);
            TriangulateEdgeStrip(
                e1, weights1, cell.Index,
                e2, weights2, neighbor.Index, hasRoad
            );
        }
        //after all other connection work is done, right before we move on to the corner triangle. 
        //We'll leave it to the feature manager to decide whether a wall should actually be placed.
        features.AddWall(e1, cell, e2, neighbor, hasRiver, hasRoad);

        HexCell nextNeighbor = cell.GetNeighbor(direction.Next());
        if (direction <= HexDirection.E && nextNeighbor != null)
        {
            Vector3 v5 = e1.v5 + HexMetrics.GetBridge(direction.Next());
            v5.y = nextNeighbor.Position.y;

            //figure out what the lowest cell is.
            if (cell.Elevation <= neighbor.Elevation)
            {
                if (cell.Elevation <= nextNeighbor.Elevation)
                {
                    TriangulateCorner(
                        e1.v5, cell, e2.v5, neighbor, v5, nextNeighbor
                    );
                }
                else
                {
                    //it means that the next neighbor is the lowest cell.
                    //We have to rotate the triangle counterclockwise to keep it correctly oriented.
                    TriangulateCorner(
                        v5, nextNeighbor, e1.v5, cell, e2.v5, neighbor
                    );
                }
            }
            // first check already failed, it becomes a contest between the two neighboring cells. 
            // If the edge neighbor is the lowest, then we have to rotate clockwise, otherwise counterclockwise.
            else if (neighbor.Elevation <= nextNeighbor.Elevation)
            {
                TriangulateCorner(
                    e2.v5, neighbor, v5, nextNeighbor, e1.v5, cell
                );

            }
            else
            {
                TriangulateCorner(
                    v5, nextNeighbor, e1.v5, cell, e2.v5, neighbor
                );
            }
        }
    }

    void TriangulateEdgeTerraces(
        EdgeVertices begin, HexCell beginCell,
        EdgeVertices end, HexCell endCell,
        bool hasRoad
    )
    {
        EdgeVertices e2 = EdgeVertices.TerraceLerp(begin, end, 1);
        Color w2 = HexMetrics.TerraceLerp(weights1, weights2, 1);

        float i1 = beginCell.Index;
        float i2 = endCell.Index;

        TriangulateEdgeStrip(begin, weights1, i1, e2, w2, i2, hasRoad);

        for (int i = 2; i < HexMetrics.terraceSteps; i++)
        {
            EdgeVertices e1 = e2;
            Color w1 = w2;
            e2 = EdgeVertices.TerraceLerp(begin, end, i);
            w2 = HexMetrics.TerraceLerp(weights1, weights2, i);
            TriangulateEdgeStrip(e1, w1, i1, e2, w2, i2, hasRoad);
        }

        TriangulateEdgeStrip(e2, w2, i1, end, weights2, i2, hasRoad);
    }

    void TriangulateAdjacentToRiver(
       HexDirection direction, HexCell cell, Vector3 center, EdgeVertices e
   )
    {
        //to take care of roads so that road and rivers can co-exist
        if (cell.HasRoads)
        {
            TriangulateRoadAdjacentToRiver(direction, cell, center, e);
        }

        if (cell.HasRiverThroughEdge(direction.Next()))
        {
            if (cell.HasRiverThroughEdge(direction.Previous()))
            {
                center += HexMetrics.GetSolidEdgeMiddle(direction) *
                    (HexMetrics.innerToOuter * 0.5f);
            }
            else if (
                    cell.HasRiverThroughEdge(direction.Previous2())
                )
            {
                center += HexMetrics.GetFirstSolidCorner(direction) * 0.25f;
            }
        }
        else if (
            cell.HasRiverThroughEdge(direction.Previous()) &&
            cell.HasRiverThroughEdge(direction.Next2())
        )
        {
            center += HexMetrics.GetSecondSolidCorner(direction) * 0.25f;
        }


        EdgeVertices m = new EdgeVertices(
            Vector3.Lerp(center, e.v1, 0.5f),
            Vector3.Lerp(center, e.v5, 0.5f)
        );

        TriangulateEdgeStrip(
            m, weights1, cell.Index,
			e, weights1, cell.Index
        );
        TriangulateEdgeFan(center, e, cell.Index);

        //produce features when not in water or road
        if (!cell.IsUnderwater && !cell.HasRoadThroughEdge(direction))
        {
            features.AddFeature(cell, (center + e.v1 + e.v5) * (1f / 3f));
        }
    }

    void TriangulateRoadAdjacentToRiver(
        HexDirection direction, HexCell cell, Vector3 center, EdgeVertices e
    )
    {
        //Check whether a road goes through the current edge, 
        //get the interpolators, create the middle vertices, and invoke TriangulateRoad. 
        bool hasRoadThroughEdge = cell.HasRoadThroughEdge(direction);
        bool previousHasRiver = cell.HasRiverThroughEdge(direction.Previous());
        bool nextHasRiver = cell.HasRiverThroughEdge(direction.Next());
        Vector2 interpolators = GetRoadInterpolators(direction, cell);
        Vector3 roadCenter = center;

        //to push the road center in the opposite direction. 
        //Moving a third of the way towards the middle edge in that direction does the trick.
        if (cell.HasRiverBeginOrEnd)
        {
            roadCenter += HexMetrics.GetSolidEdgeMiddle(
                cell.RiverBeginOrEndDirection.Opposite()
            ) * (1f / 3f);
        } //If the cell doesn't have the beginning or end of a river, 
          //we can check whether the incoming and outgoing rivers go in opposite directions. 
        else if (cell.IncomingRiver == cell.OutgoingRiver.Opposite())
        {
            Vector3 corner;
            //If so, we have a straight river.
            if (previousHasRiver)
            {
                if (
                    !hasRoadThroughEdge &&
                    !cell.HasRoadThroughEdge(direction.Next())
                )
                {
                    return;
                }
                corner = HexMetrics.GetSecondSolidCorner(direction);
            }
            else
            {
                if (
                    !hasRoadThroughEdge &&
                    !cell.HasRoadThroughEdge(direction.Previous())
                )
                {
                    return;
                }
                corner = HexMetrics.GetFirstSolidCorner(direction);
            }

            roadCenter += corner * 0.5f;
            //ensure that only one bridge instance is generated per cell. 
            if (cell.IncomingRiver == direction.Next() && (
                cell.HasRoadThroughEdge(direction.Next2()) ||
                cell.HasRoadThroughEdge(direction.Opposite())
            ))
            {
                //place bridge adjacent to rivers when making roads
                features.AddBridge(roadCenter, center - corner * 0.5f);

            }
            center += corner * 0.25f;
        } // check for zigzag
          //compare the directions of the incoming and outgoing rivers. If they're adjacent, then we have a zigzag
        else if (cell.IncomingRiver == cell.OutgoingRiver.Previous())
        { //based on flow direction
            roadCenter -= HexMetrics.GetSecondCorner(cell.IncomingRiver) * 0.2f;
        }
        else if (cell.IncomingRiver == cell.OutgoingRiver.Next())
        {
            roadCenter -= HexMetrics.GetFirstCorner(cell.IncomingRiver) * 0.2f;
        }
        //When there's a river on both sides of the current direction, 
        //then we're on the inside of a curve.
        else if (previousHasRiver && nextHasRiver)
        {
            if (!hasRoadThroughEdge)
            { //prune isolated road parts
                return;
            }
            //have to pull the road center towards the current cell edge, shortening the road by a lot. 
            //A factor of 0.7 is fine. The cell center has to move as well, with a factor of 0.5.
            Vector3 offset = HexMetrics.GetSolidEdgeMiddle(direction) *
                HexMetrics.innerToOuter;
            roadCenter += offset * 0.7f;
            center += offset * 0.5f;
        }
        else
        { //checking scenario for outside of a curving river
            HexDirection middle; //get middle direction
            if (previousHasRiver)
            {
                middle = direction.Next();
            }
            else if (nextHasRiver)
            {
                middle = direction.Previous();
            }
            else
            {
                middle = direction;
            }
            if (//prune roads on this side of the river as well. 
                //check all three directions for a road, relative to the middle. If there is no road, abort.
                !cell.HasRoadThroughEdge(middle) &&
                !cell.HasRoadThroughEdge(middle.Previous()) &&
                !cell.HasRoadThroughEdge(middle.Next())
            )
            {
                return;
            }
            Vector3 offset = HexMetrics.GetSolidEdgeMiddle(middle);
            //move the road center towards that edge by a factor of 0.25.
            roadCenter += offset * 0.25f;

            //add bridge curving rivers
            if (direction == middle &&
                cell.HasRoadThroughEdge(direction.Opposite()))
            {
                features.AddBridge(
                    roadCenter,
                    center - offset * (HexMetrics.innerToOuter * 0.7f)
                );
            }
        }

        Vector3 mL = Vector3.Lerp(roadCenter, e.v1, interpolators.x);
        Vector3 mR = Vector3.Lerp(roadCenter, e.v5, interpolators.y);
        TriangulateRoad(roadCenter, mL, mR, e, hasRoadThroughEdge, cell.Index);


        if (previousHasRiver)
        {
            TriangulateRoadEdge(roadCenter, center, mL, cell.Index);
        }
        if (nextHasRiver)
        {
            TriangulateRoadEdge(roadCenter, mR, center, cell.Index);
        }
    }

    void TriangulateCorner(
        Vector3 bottom, HexCell bottomCell,
        Vector3 left, HexCell leftCell,
        Vector3 right, HexCell rightCell
    )
    {
        //to determine the types of the left and right edges.
        HexEdgeType leftEdgeType = bottomCell.GetEdgeType(leftCell);
        HexEdgeType rightEdgeType = bottomCell.GetEdgeType(rightCell);

        //Check whether we are in slope-slope-flat
        if (leftEdgeType == HexEdgeType.Slope)
        {
            if (rightEdgeType == HexEdgeType.Slope)
            {
                TriangulateCornerTerraces(
                    bottom, bottomCell, left, leftCell, right, rightCell
                );
            }

            //If the right edge is flat, then we have to beginterracing from the left instead of the bottom.
            else if (rightEdgeType == HexEdgeType.Flat)
            {
                TriangulateCornerTerraces(
                    left, leftCell, right, rightCell, bottom, bottomCell
                );
            }
            else
            {
                TriangulateCornerTerracesCliff(
                    bottom, bottomCell, left, leftCell, right, rightCell
                );
            }
        }

        //If the left edge is flat, then we have to begin from the right.
        else if (rightEdgeType == HexEdgeType.Slope)
        {
            if (leftEdgeType == HexEdgeType.Flat)
            {
                TriangulateCornerTerraces(
                    right, rightCell, bottom, bottomCell, left, leftCell
                );
            }
            else
            {
                TriangulateCornerCliffTerraces(
                    bottom, bottomCell, left, leftCell, right, rightCell
                );
            }
        }

        else if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            if (leftCell.Elevation < rightCell.Elevation)
            {
                TriangulateCornerCliffTerraces(
                    right, rightCell, bottom, bottomCell, left, leftCell
                );
            }
            else
            {
                TriangulateCornerTerracesCliff(
                    left, leftCell, right, rightCell, bottom, bottomCell
                );
            }
        }
        else
        {
            terrain.AddTriangle(bottom, left, right);
            Vector3 indices;
            indices.x = bottomCell.Index;
            indices.y = leftCell.Index;
            indices.z = rightCell.Index;
            terrain.AddTriangleCellData(indices, weights1, weights2, weights3);
        }

        //add the corner segments
        features.AddWall(bottom, bottomCell, left, leftCell, right, rightCell);
    }

    void TriangulateCornerTerraces(
       Vector3 begin, HexCell beginCell,
       Vector3 left, HexCell leftCell,
       Vector3 right, HexCell rightCell
   )
    {
        Vector3 v3 = HexMetrics.TerraceLerp(begin, left, 1);
        Vector3 v4 = HexMetrics.TerraceLerp(begin, right, 1);
        Color w3 = HexMetrics.TerraceLerp(weights1, weights2, 1);
        Color w4 = HexMetrics.TerraceLerp(weights1, weights3, 1);

        Vector3 indices;
        indices.x = beginCell.Index;
        indices.y = leftCell.Index;
        indices.z = rightCell.Index;

        terrain.AddTriangleCellData(indices, weights1, w3, w4);


        for (int i = 2; i < HexMetrics.terraceSteps; i++)
        {
            Vector3 v1 = v3;
            Vector3 v2 = v4;
            Color w1 = w3;
            Color w2 = w4;
            v3 = HexMetrics.TerraceLerp(begin, left, i);
            v4 = HexMetrics.TerraceLerp(begin, right, i);
            w3 = HexMetrics.TerraceLerp(weights1, weights2, i);
            w4 = HexMetrics.TerraceLerp(weights1, weights3, i);
            terrain.AddQuad(v1, v2, v3, v4);
            terrain.AddQuadCellData(indices, w1, w2, w3, w4);
        }

        terrain.AddQuad(v3, v4, left, right);
        terrain.AddQuadCellData(indices, w3, w4, weights2, weights3);
    }

    //take care of both slope-cliff cases at once.
    //Merging Slopes and Cliffs
    void TriangulateCornerTerracesCliff(
            Vector3 begin, HexCell beginCell,
            Vector3 left, HexCell leftCell,
            Vector3 right, HexCell rightCell
        )
    {
        //make connection on side of triangle from 2 bottom to side on the right
        float b = 1f / (rightCell.Elevation - beginCell.Elevation);

        //make sure that the interpolators are always positive.
        if (b < 0)
        {
            b = -b;
        }

        Vector3 boundary = Vector3.Lerp(
            HexMetrics.Perturb(begin), HexMetrics.Perturb(right), b
        );
        Color boundaryWeights = Color.Lerp(weights1, weights3, b);
        Vector3 indices;
        indices.x = beginCell.Index;
        indices.y = leftCell.Index;
        indices.z = rightCell.Index;


        //If the top edge is a slope, we again need to connect terraces and a cliff.
        //does the bottom part
        TriangulateBoundaryTriangle(
            begin, weights1, left, weights2, boundary, boundaryWeights, indices
        );

        //completes the top part by terrain.Add a rotated boundary triangle if there is a slope.
        if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            TriangulateBoundaryTriangle(
                left, weights2, right, weights3,
                boundary, boundaryWeights, indices
            );
        }
        else
        {
            //Otherwise a simple triangle suffices.
            terrain.AddTriangleUnperturbed(
                HexMetrics.Perturb(left), HexMetrics.Perturb(right), boundary
            );
            terrain.AddTriangleCellData(
                indices, weights2, weights3, boundaryWeights
            );
        }
    }

    void TriangulateCornerCliffTerraces(
        Vector3 begin, HexCell beginCell,
        Vector3 left, HexCell leftCell,
        Vector3 right, HexCell rightCell
    )
    {
        //make connection on side of triangle from 2 bottom to side on the right
        float b = 1f / (leftCell.Elevation - beginCell.Elevation);

        //make sure that the interpolators are always positive.
        if (b < 0)
        {
            b = -b;
        }

        Vector3 boundary = Vector3.Lerp(
            HexMetrics.Perturb(begin), HexMetrics.Perturb(left), b
        );
        Color boundaryWeights = Color.Lerp(weights1, weights2, b);
        Vector3 indices;
        indices.x = beginCell.Index;
        indices.y = leftCell.Index;
        indices.z = rightCell.Index;

        //If the top edge is a slope, we again need to connect terraces and a cliff.
        //does the bottom part
        TriangulateBoundaryTriangle(
            right, weights3, begin, weights1, boundary, boundaryWeights, indices
        );

        //completes the top part by terrain.Add a rotated boundary triangle if there is a slope.
        if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            TriangulateBoundaryTriangle(
                left, weights2, right, weights3,
                boundary, boundaryWeights, indices
            );
        }
        else
        {
            //Otherwise a simple triangle suffices.
            terrain.AddTriangleUnperturbed(
                HexMetrics.Perturb(left), HexMetrics.Perturb(right), boundary
            );
            terrain.AddTriangleCellData(
                indices, weights2, weights3, boundaryWeights
            );
        }
    }

    void TriangulateBoundaryTriangle(
        Vector3 begin, Color beginWeights,
        Vector3 left, Color leftWeights,
        Vector3 boundary, Color boundaryWeights, Vector3 indices
   )
    {
        //triangulating the terraces.
        Vector3 v2 = HexMetrics.Perturb(HexMetrics.TerraceLerp(begin, left, 1));
        Color w2 = HexMetrics.TerraceLerp(beginWeights, leftWeights, 1);

        terrain.AddTriangleUnperturbed(HexMetrics.Perturb(begin), v2, boundary);
        terrain.AddTriangleCellData(indices, beginWeights, w2, boundaryWeights);

        for (int i = 2; i < HexMetrics.terraceSteps; i++)
        {
            Vector3 v1 = v2;
            Color w1 = w2;
            v2 = HexMetrics.Perturb(HexMetrics.TerraceLerp(begin, left, i));
            w2 = HexMetrics.TerraceLerp(beginWeights, leftWeights, i);
            terrain.AddTriangleUnperturbed(v1, v2, boundary);
            terrain.AddTriangleCellData(indices, w1, w2, boundaryWeights);
        }

        terrain.AddTriangleUnperturbed(v2, HexMetrics.Perturb(left), boundary);
        terrain.AddTriangleCellData(indices, w2, leftWeights, boundaryWeights);
    }

    void TriangulateEdgeFan(Vector3 center, EdgeVertices edge, float index)
    {
        terrain.AddTriangle(center, edge.v1, edge.v2);
		terrain.AddTriangle(center, edge.v2, edge.v3);
		terrain.AddTriangle(center, edge.v3, edge.v4);
		terrain.AddTriangle(center, edge.v4, edge.v5);

		Vector3 indices;
		indices.x = indices.y = indices.z = index;
		terrain.AddTriangleCellData(indices, weights1);
		terrain.AddTriangleCellData(indices, weights1);
		terrain.AddTriangleCellData(indices, weights1);
		terrain.AddTriangleCellData(indices, weights1);
    }

    void TriangulateEdgeStrip(
        EdgeVertices e1, Color w1, float index1,
        EdgeVertices e2, Color w2, float index2,
        bool hasRoad = false
    )
    {
        terrain.AddQuad(e1.v1, e1.v2, e2.v1, e2.v2);
        terrain.AddQuad(e1.v2, e1.v3, e2.v2, e2.v3);
        terrain.AddQuad(e1.v3, e1.v4, e2.v3, e2.v4);
        terrain.AddQuad(e1.v4, e1.v5, e2.v4, e2.v5);

        Vector3 indices;
        indices.x = indices.z = index1;
        indices.y = index2;
        terrain.AddQuadCellData(indices, w1, w2);
        terrain.AddQuadCellData(indices, w1, w2);
        terrain.AddQuadCellData(indices, w1, w2);
        terrain.AddQuadCellData(indices, w1, w2);


        if (hasRoad)
        {
            TriangulateRoadSegment(
                e1.v2, e1.v3, e1.v4, e2.v2, e2.v3, e2.v4, w1, w2, indices
            );
        }
    }

    void TriangulateRiverQuad(
        Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        float y, float v, bool reversed, Vector3 indices
    )
    {
        TriangulateRiverQuad(v1, v2, v3, v4, y, y, v, reversed, indices);
    }

    void TriangulateRiverQuad(
        Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        float y1, float y2, float v, bool reversed, Vector3 indices
    )
    {
        v1.y = v2.y = y1;
        v3.y = v4.y = y2;
        rivers.AddQuad(v1, v2, v3, v4);
        if (reversed)
        {
            rivers.AddQuadUV(1f, 0f, 0.8f - v, 0.6f - v);
        }
        else
        {
            rivers.AddQuadUV(0f, 1f, v, v + 0.2f);
        }

        rivers.AddQuadCellData(indices, weights1, weights2);
    }

    void TriangulateRoadSegment(
        Vector3 v1, Vector3 v2, Vector3 v3,
        Vector3 v4, Vector3 v5, Vector3 v6,
        Color w1, Color w2, Vector3 indices
    )
    {
        roads.AddQuad(v1, v2, v4, v5);
        roads.AddQuad(v2, v3, v5, v6);
        roads.AddQuadUV(0f, 1f, 0f, 0f);
        roads.AddQuadUV(1f, 0f, 0f, 0f);

        roads.AddQuadCellData(indices, w1, w2);
        roads.AddQuadCellData(indices, w1, w2);
    }

    //need to know the cell's center, the left and right middle vertices, and the edge vertices
    void TriangulateRoad(
        Vector3 center, Vector3 mL, Vector3 mR,
        EdgeVertices e, bool hasRoadThroughCellEdge, float index
    )
    {
        if (hasRoadThroughCellEdge)
        {
            Vector3 indices;
            indices.x = indices.y = indices.z = index;
            //need one additional vertex to construct the road segment. 
            //It sits between the left and right middle vertices.
            Vector3 mC = Vector3.Lerp(mL, mR, 0.5f);
            TriangulateRoadSegment(mL, mC, mR, e.v2, e.v3, e.v4,
                weights1, weights1, indices);

            //add the remaining two triangles.
            roads.AddTriangle(center, mL, mC);
            roads.AddTriangle(center, mC, mR);

            //add the UV coordinates of the triangles
            //Two of their vertices sit in the middle of the road, 
            roads.AddTriangleUV(
                new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(1f, 0f)
            );
            //the other at its edge.
            roads.AddTriangleUV(
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f)
            );
            roads.AddTriangleCellData(indices, weights1);
            roads.AddTriangleCellData(indices, weights1);
        }
        else
        {
            TriangulateRoadEdge(center, mL, mR, index);
        }
    }

    void TriangulateRoadEdge(Vector3 center, Vector3 mL, Vector3 mR, float index)
    {
        roads.AddTriangle(center, mL, mR);
        roads.AddTriangleUV(
            new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f)
        );

        Vector3 indices;
        indices.x = indices.y = indices.z = index;
        roads.AddTriangleCellData(indices, weights1);
    }

    Vector2 GetRoadInterpolators(HexDirection direction, HexCell cell)
    {
        Vector2 interpolators;
        //If there's a road going in the current direction, put the points halfway.
        if (cell.HasRoadThroughEdge(direction))
        {
            interpolators.x = interpolators.y = 0.5f;
        }
        else
        { //Otherwise, it depends. For the left point, we can use ½ when there's a road going through the previous direction. 
          //If not, we should use ¼. The same goes for the right point, but with the next direction.
            interpolators.x =
                cell.HasRoadThroughEdge(direction.Previous()) ? 0.5f : 0.25f;
            interpolators.y =
                cell.HasRoadThroughEdge(direction.Next()) ? 0.5f : 0.25f;
        }
        return interpolators;
    }

    void TriangulateWaterfallInWater(
        Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        float y1, float y2, float waterY, Vector3 indices
    )
    {
        v1.y = v2.y = y1;
        v3.y = v4.y = y2;
        //PREVENT end result still not match the original waterfall.
        v1 = HexMetrics.Perturb(v1);
        v2 = HexMetrics.Perturb(v2);
        v3 = HexMetrics.Perturb(v3);
        v4 = HexMetrics.Perturb(v4);
        //o move the bottom vertices upward, divide their distance below the water surface by the height 
        //of the waterfall. That gives us the interpolator value.
        float t = (waterY - y2) / (y1 - y2);
        v3 = Vector3.Lerp(v3, v1, t);
        v4 = Vector3.Lerp(v4, v2, t);
        rivers.AddQuadUnperturbed(v1, v2, v3, v4);
        rivers.AddQuadUV(0f, 1f, 0.8f, 1f);
        rivers.AddQuadCellData(indices, weights1, weights2);
    }
}

