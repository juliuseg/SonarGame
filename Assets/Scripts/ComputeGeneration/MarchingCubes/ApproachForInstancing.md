I have a computer shader that does marching cubes. Chunk size is set to 16x16x16. I get the triangles and construct a mesh. 

Now I want to do some instancing for objects. I want to take all the triangles whose normal is pointing: upwards, downwards, sideways and everything. So I could create a struct of triangle positions and their normal vector. 

Then I read that on the cpu. I want to create a subset of these structs based on normal direction (I only want mesh to spawn if   and their normals point in a specific direction). Then use that to assign to an array I set on a material to create positions+rotstions(based on normals) for gpu instancing. 

Is this feasible. I’m worried about the runtime of: 
Having another struct on my shader (I could use appendbuffer so the allocation is still high but readback to cpu is faster)
Picking points for instancing at random and based on normals (potentially lots of calculations of normals? Maybe solved by adding a float to the struct and getting the direction of the normal with respect to the up vector and then do easier random picking? Picking this would just be On, since I just walk through array to find candidates)

See if you can guess if there are other problems. Then use those and the ones I’m worried about and tell me if it’s not a problem or there is an alternative that is faster!





For the 