namespace Fusion360.Mcp.Core.Units;

/// <summary>
/// Pure geometry helpers for <c>create_hole</c>. Deliberately free of
/// adsk references so the face-picking and plane-solving rules can be
/// unit-tested without a running Fusion instance. Port of reference
/// addon/server/hole_geometry.py. All lengths in centimetres.
/// </summary>
public static class HoleGeometry
{
    /// <summary>
    /// Minimum |normal.z| for a planar face to count as horizontal, i.e. to
    /// be a candidate "top"/"bottom" face. 0.99995 allows roughly 0.6
    /// degrees of tilt.
    /// </summary>
    public const double HorizontalFaceMinNz = 0.99995;

    /// <summary>
    /// True if a face with this outward normal faces up (or down) and is
    /// flat. Comparing bounding boxes instead would also accept the side
    /// faces of a box, whose bounding boxes reach the body's top too.
    /// </summary>
    public static bool IsHorizontalFace(double normalZ, bool wantUp, double minNz = HorizontalFaceMinNz)
        => (wantUp ? normalZ : -normalZ) >= minNz;

    /// <summary>
    /// Sort key picking the highest (or lowest) face, ties going to the
    /// larger area. *edgeZ* is the face's own extreme Z (top of its
    /// bounding box when looking for a top face). Within the tilt
    /// tolerance that is not the same as the Z of an arbitrary point on
    /// the face.
    /// </summary>
    public static (double Rank, double Area) FaceRankKey(double edgeZ, double area, bool wantUp)
        => (wantUp ? edgeZ : -edgeZ, area);

    /// <summary>
    /// Z where the vertical line through (x, y) meets the plane given by
    /// its outward normal and origin. Any point on the face has the right
    /// Z only if the face is exactly horizontal, so solve the plane
    /// instead of reusing a point on the face.
    /// </summary>
    /// <exception cref="ArgumentException">The plane is vertical: no unique Z above (x, y).</exception>
    public static double PlaneZAt(
        (double Nx, double Ny, double Nz) normal,
        (double Ox, double Oy, double Oz) origin,
        double x,
        double y)
    {
        if (normal.Nz == 0)
        {
            throw new ArgumentException("plane is vertical: no unique Z above (x, y)");
        }

        return origin.Oz - (normal.Nx * (x - origin.Ox) + normal.Ny * (y - origin.Oy)) / normal.Nz;
    }
}
